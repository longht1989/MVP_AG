using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using WindowsVideoPreview.Models;
using WindowsVideoPreview.Services;

namespace WindowsVideoPreview.Views;

public partial class VideoPlayerControl : System.Windows.Controls.UserControl
{
    private MpvPlayer? _player;
    private System.Windows.Threading.DispatcherTimer? _zoomToastTimer;
    private readonly WinFormsMouseWheelFilter _wheelFilter;

    public event Action<string>? FileDropped;
    public event Action? OpenRequested;
    public event Action? CloseRequested;

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll")]
    private static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint crKey, byte bAlpha, uint dwFlags);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern IntPtr CreateRectRgn(int x1, int y1, int x2, int y2);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowRgn(IntPtr hWnd, IntPtr hRgn, bool bRedraw);

    private double _currentWipeRatio = 1.0;

    public string RoleName
    {
        get => RoleText.Text;
        set
        {
            RoleText.Text = value;
            DropTargetHintText.Text = $"Will load into {value}";
        }
    }

    public System.Windows.Media.Color RoleColor
    {
        set => RoleBadge.Background = new SolidColorBrush(value);
    }

    public bool ShowCloseButton
    {
        get => CloseButton.Visibility == Visibility.Visible;
        set => CloseButton.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
    }

    public VideoPlayerControl()
    {
        InitializeComponent();
        _wheelFilter = new WinFormsMouseWheelFilter(this);
        try
        {
            System.Windows.Forms.Application.AddMessageFilter(_wheelFilter);
        }
        catch { }

        Unloaded += (_, _) =>
        {
            try
            {
                System.Windows.Forms.Application.RemoveMessageFilter(_wheelFilter);
            }
            catch { }
        };

        PreviewMouseWheel += OnWpfPreviewMouseWheel;

        VideoPanel.MouseDoubleClick += (_, e) =>
        {
            if (e.Button == System.Windows.Forms.MouseButtons.Left && _player != null && Math.Abs(_player.ZoomFactor - 1.0) > 0.01)
            {
                Dispatcher.Invoke(() => ResetZoom());
            }
        };

        Loaded += (_, _) =>
        {
            if (FormsHost.Handle != IntPtr.Zero && _currentWipeRatio < 0.998)
            {
                SetWipeClip(_currentWipeRatio);
            }
        };
        SizeChanged += (_, _) =>
        {
            if (FormsHost.Handle != IntPtr.Zero && _currentWipeRatio < 0.998 && Visibility == Visibility.Visible)
            {
                SetWipeClip(_currentWipeRatio);
            }
        };
    }

    public void AttachPlayer(MpvPlayer player)
    {
        _player = player;
        _player.ZoomChanged += factor =>
        {
            Dispatcher.Invoke(() =>
            {
                int percent = (int)Math.Round(factor * 100);
                ZoomBadgeText.Text = $"{percent}%";
                ZoomBadge.Visibility = Math.Abs(factor - 1.0) > 0.01 ? Visibility.Visible : Visibility.Collapsed;
            });
        };

        if (VideoPanel.IsHandleCreated)
        {
            _player.Initialize(VideoPanel.Handle);
        }
        else
        {
            VideoPanel.HandleCreated += (_, _) => _player.Initialize(VideoPanel.Handle);
        }
    }

    public void SetWipeClip(double splitRatio)
    {
        double clamped = Math.Clamp(splitRatio, 0.0, 1.0);
        _currentWipeRatio = clamped;

        if (clamped <= 0.002)
        {
            // 0% -> Video A only
            Visibility = Visibility.Collapsed;
            return;
        }

        Visibility = Visibility.Visible;

        if (FormsHost.Handle == IntPtr.Zero) return;

        try
        {
            int w = (int)Math.Max(1, ActualWidth);
            int h = (int)Math.Max(1, ActualHeight);

            if (clamped >= 0.998)
            {
                // 100% -> Full Video B, no clipping
                SetWindowRgn(FormsHost.Handle, IntPtr.Zero, true);
            }
            else
            {
                // Split Wipe: Left 0 -> splitX shows Video A underneath
                // Right splitX -> w shows Video B
                int splitX = (int)Math.Round(clamped * w);
                IntPtr hRgn = CreateRectRgn(splitX, 0, w, h);
                SetWindowRgn(FormsHost.Handle, hRgn, true);
            }
        }
        catch { }
    }

    public void ResetClip()
    {
        _currentWipeRatio = 1.0;
        Visibility = Visibility.Visible;
        if (FormsHost.Handle != IntPtr.Zero)
        {
            try
            {
                SetWindowRgn(FormsHost.Handle, IntPtr.Zero, true);
            }
            catch { }
        }
    }

    public void SetWindowOpacity(double opacity)
    {
        SetWipeClip(opacity);
    }

    public void UpdateMediaInfo(MediaItem? item, MediaMetadata? meta, string timecode, string frameText)
    {
        if (item == null)
        {
            PlaceholderGrid.Visibility = Visibility.Visible;
            TopHeaderOverlay.Visibility = Visibility.Collapsed;
            BottomOverlay.Visibility = Visibility.Collapsed;
            return;
        }

        PlaceholderGrid.Visibility = Visibility.Collapsed;
        TopHeaderOverlay.Visibility = Visibility.Visible;
        BottomOverlay.Visibility = Visibility.Visible;

        FileNameText.Text = item.FileName;
        TimecodeText.Text = timecode;
        FrameCountText.Text = frameText;

        if (meta != null)
        {
            ResolutionBadge.Text = meta.Resolution;
            FpsBadge.Text = meta.FpsText;
        }
        else
        {
            ResolutionBadge.Text = item.Resolution;
            FpsBadge.Text = item.FpsText;
        }
    }

    public void UpdateTimecodeAndFrame(string timecode, string frameText)
    {
        TimecodeText.Text = timecode;
        FrameCountText.Text = frameText;
    }

    private void OnDragEnter(object sender, System.Windows.DragEventArgs e)
    {
        if (HasValidDropPayload(e))
        {
            e.Effects = System.Windows.DragDropEffects.Copy;
            DropHighlightBorder.Visibility = Visibility.Visible;
        }
        else
        {
            e.Effects = System.Windows.DragDropEffects.None;
        }
        e.Handled = true;
    }

    private void OnDragOver(object sender, System.Windows.DragEventArgs e)
    {
        if (HasValidDropPayload(e))
        {
            e.Effects = System.Windows.DragDropEffects.Copy;
            DropHighlightBorder.Visibility = Visibility.Visible;
        }
        else
        {
            e.Effects = System.Windows.DragDropEffects.None;
            DropHighlightBorder.Visibility = Visibility.Collapsed;
        }
        e.Handled = true;
    }

    private void OnDragLeave(object sender, System.Windows.DragEventArgs e)
    {
        DropHighlightBorder.Visibility = Visibility.Collapsed;
        e.Handled = true;
    }

    private void OnDrop(object sender, System.Windows.DragEventArgs e)
    {
        DropHighlightBorder.Visibility = Visibility.Collapsed;

        string? filePath = ExtractFilePathFromDrop(e);
        if (!string.IsNullOrEmpty(filePath) && File.Exists(filePath))
        {
            FileDropped?.Invoke(filePath);
        }
        e.Handled = true;
    }

    private static bool HasValidDropPayload(System.Windows.DragEventArgs e)
    {
        if (e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop)) return true;
        if (e.Data.GetDataPresent(System.Windows.DataFormats.UnicodeText)) return true;
        if (e.Data.GetDataPresent(System.Windows.DataFormats.Text)) return true;
        if (e.Data.GetDataPresent(typeof(MediaItem))) return true;
        return false;
    }

    private static string? ExtractFilePathFromDrop(System.Windows.DragEventArgs e)
    {
        if (e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop))
        {
            var files = (string[])e.Data.GetData(System.Windows.DataFormats.FileDrop);
            if (files != null && files.Length > 0) return files[0];
        }

        if (e.Data.GetDataPresent(typeof(MediaItem)))
        {
            var item = e.Data.GetData(typeof(MediaItem)) as MediaItem;
            if (item != null) return item.FilePath;
        }

        if (e.Data.GetDataPresent(System.Windows.DataFormats.UnicodeText))
        {
            var text = e.Data.GetData(System.Windows.DataFormats.UnicodeText) as string;
            if (!string.IsNullOrEmpty(text)) return text;
        }

        if (e.Data.GetDataPresent(System.Windows.DataFormats.Text))
        {
            var text = e.Data.GetData(System.Windows.DataFormats.Text) as string;
            if (!string.IsNullOrEmpty(text)) return text;
        }

        return null;
    }

    private void OnOpenClick(object sender, RoutedEventArgs e)
    {
        OpenRequested?.Invoke();
    }

    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        CloseRequested?.Invoke();
    }

    private void OnPlaceholderMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            OpenRequested?.Invoke();
        }
    }

    private void OnWpfPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            if (e.Delta > 0) ZoomIn();
            else if (e.Delta < 0) ZoomOut();
            e.Handled = true;
        }
    }

    public void ZoomIn()
    {
        if (_player == null) return;
        _player.AdjustZoom(1.15);
        ShowZoomToast(_player.ZoomFactor);
    }

    public void ZoomOut()
    {
        if (_player == null) return;
        _player.AdjustZoom(1.0 / 1.15);
        ShowZoomToast(_player.ZoomFactor);
    }

    public void ResetZoom()
    {
        if (_player == null) return;
        _player.ResetZoom();
        ShowZoomToast(1.0);
    }

    private void ShowZoomToast(double factor)
    {
        int percent = (int)Math.Round(factor * 100);
        ZoomToastText.Text = $"Zoom: {percent}%";
        ZoomBadgeText.Text = $"{percent}%";
        ZoomBadge.Visibility = Math.Abs(factor - 1.0) > 0.01 ? Visibility.Visible : Visibility.Collapsed;

        ZoomToast.Visibility = Visibility.Visible;
        if (_zoomToastTimer == null)
        {
            _zoomToastTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(1200)
            };
            _zoomToastTimer.Tick += (_, _) =>
            {
                _zoomToastTimer.Stop();
                ZoomToast.Visibility = Visibility.Collapsed;
            };
        }
        _zoomToastTimer.Stop();
        _zoomToastTimer.Start();
    }

    private void OnZoomBadgeMouseDown(object sender, MouseButtonEventArgs e)
    {
        ResetZoom();
        e.Handled = true;
    }

    private class WinFormsMouseWheelFilter : System.Windows.Forms.IMessageFilter
    {
        private readonly VideoPlayerControl _control;
        public WinFormsMouseWheelFilter(VideoPlayerControl control) => _control = control;

        public bool PreFilterMessage(ref System.Windows.Forms.Message m)
        {
            const int WM_MOUSEWHEEL = 0x020A;
            if (m.Msg == WM_MOUSEWHEEL)
            {
                bool isCtrl = (System.Windows.Forms.Control.ModifierKeys & System.Windows.Forms.Keys.Control) == System.Windows.Forms.Keys.Control ||
                              (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;
                if (isCtrl)
                {
                    try
                    {
                        var panel = _control.VideoPanel;
                        if (panel != null && panel.IsHandleCreated)
                        {
                            var screenPt = System.Windows.Forms.Cursor.Position;
                            var clientPt = panel.PointToClient(screenPt);
                            if (panel.ClientRectangle.Contains(clientPt))
                            {
                                int delta = (short)((m.WParam.ToInt64() >> 16) & 0xFFFF);
                                _control.Dispatcher.Invoke(() =>
                                {
                                    if (delta > 0) _control.ZoomIn();
                                    else if (delta < 0) _control.ZoomOut();
                                });
                                return true;
                            }
                        }
                    }
                    catch { }
                }
            }
            return false;
        }
    }
}

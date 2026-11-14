using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using WindowsVideoPreview.Models;
using WindowsVideoPreview.Services;
using WindowsVideoPreview.ViewModels;
using WindowsVideoPreview.Views;

namespace WindowsVideoPreview;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;
    private readonly PlaybackCoordinator _coordinator;
    private readonly MpvPlayer _playerA;
    private readonly MpvPlayer _playerB;
    private readonly FfmpegService _ffmpegService;
    private readonly WaveformService _waveformService;
    private readonly SettingsService _settingsService;

    private bool _isSliderDragging;
    private WindowStyle _previousWindowStyle;
    private WindowState _previousWindowState;
    private Point _playlistDragStartPoint;

    public MainWindow()
    {
        InitializeComponent();

        _ffmpegService = new FfmpegService();
        _waveformService = new WaveformService(_ffmpegService);
        _settingsService = new SettingsService();

        _playerA = new MpvPlayer();
        _playerB = new MpvPlayer();

        _coordinator = new PlaybackCoordinator(_playerA, _playerB);
        _vm = new MainViewModel(_coordinator, _ffmpegService, _waveformService, _settingsService);

        DataContext = _vm;

        Loaded += OnWindowLoaded;
        Closing += OnWindowClosing;

        // Wire video player controls
        PlayerAControl.AttachPlayer(_playerA);
        PlayerBControl.AttachPlayer(_playerB);

        PlayerAControl.FileDropped += path => _vm.LoadVideoA(path);
        PlayerBControl.FileDropped += path =>
        {
            _vm.LoadVideoB(path);
            _vm.IsCompareMode = true;
            UpdateViewportLayout();
        };

        PlayerAControl.OpenRequested += () => _vm.OpenFileACommand.Execute(null);
        PlayerBControl.OpenRequested += () => _vm.OpenFileBCommand.Execute(null);
        PlayerBControl.CloseRequested += () =>
        {
            _vm.CloseFileBCommand.Execute(null);
            UpdateViewportLayout();
        };

        // Wire layout & opacity changes
        _vm.ViewportLayoutChanged += UpdateViewportLayout;
        _vm.OverlayOpacityChanged += opacity =>
        {
            if (_vm.IsOverlay)
            {
                PlayerBControl.SetWindowOpacity(opacity);
            }
        };

        // Handle playback end / enable waveform
        _vm.FileEndedA += () => Dispatcher.Invoke(() => { });
        _vm.FileEndedB += () => Dispatcher.Invoke(() => { });

        // Keep player control overlays up to date
        _vm.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.TimecodeA) || e.PropertyName == nameof(MainViewModel.FrameTextA))
            {
                PlayerAControl.UpdateTimecodeAndFrame(_vm.TimecodeA, _vm.FrameTextA);
            }
            if (e.PropertyName == nameof(MainViewModel.TimecodeB) || e.PropertyName == nameof(MainViewModel.FrameTextB))
            {
                PlayerBControl.UpdateTimecodeAndFrame(_vm.TimecodeB, _vm.FrameTextB);
            }
            if (e.PropertyName == nameof(MainViewModel.FileA) || e.PropertyName == nameof(MainViewModel.MetadataA))
            {
                PlayerAControl.UpdateMediaInfo(_vm.FileA, _vm.MetadataA, _vm.TimecodeA, _vm.FrameTextA);
            }
            if (e.PropertyName == nameof(MainViewModel.FileB) || e.PropertyName == nameof(MainViewModel.MetadataB))
            {
                PlayerBControl.UpdateMediaInfo(_vm.FileB, _vm.MetadataB, _vm.TimecodeB, _vm.FrameTextB);
            }
            if (e.PropertyName == nameof(MainViewModel.IsFullscreen))
            {
                ApplyFullscreen(_vm.IsFullscreen);
            }
            if (e.PropertyName == nameof(MainViewModel.IsCompareMode))
            {
                UpdateViewportLayout();
            }
            if (e.PropertyName == nameof(MainViewModel.IsPlaylistVisible))
            {
                UpdatePlaylistLayout();
            }
        };

        UpdatePlaylistLayout();
        UpdateViewportLayout();
    }

    private void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        UpdateViewportLayout();

        // Check command line arguments for video files to open
        var args = Environment.GetCommandLineArgs();
        if (args.Length > 1 && File.Exists(args[1]))
        {
            _vm.LoadVideoA(args[1]);
            if (args.Length > 2 && File.Exists(args[2]))
            {
                _vm.LoadVideoB(args[2]);
            }
        }
    }

    private void UpdatePlaylistLayout()
    {
        if (_vm.IsPlaylistVisible)
        {
            PlaylistColDef.Width = new GridLength(260);
            PlaylistSplitterColDef.Width = GridLength.Auto;
        }
        else
        {
            PlaylistColDef.Width = new GridLength(0);
            PlaylistSplitterColDef.Width = new GridLength(0);
        }
    }

    private void UpdateViewportLayout()
    {
        if (!_vm.IsCompareMode)
        {
            // SINGLE MODE: Video A gets 100% full viewport
            Grid.SetColumn(PlayerAControl, 0);
            Grid.SetColumnSpan(PlayerAControl, 3);
            ColVideoA.Width = new GridLength(1, GridUnitType.Star);
            ColVideoSplitter.Width = new GridLength(0);
            ColVideoB.Width = new GridLength(0);
            CompareSplitter.Visibility = Visibility.Collapsed;

            PlayerAControl.Width = double.NaN;
            PlayerAControl.HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch;
            PlayerAControl.VerticalAlignment = System.Windows.VerticalAlignment.Stretch;
            PlayerAControl.ResetClip();

            PlayerBControl.Visibility = Visibility.Collapsed;
            PlayerBControl.ResetClip();
        }
        else if (_vm.CompareLayout == CompareLayoutMode.Overlay)
        {
            // SPLIT WIPE MODE: Both stacked at 100% full bounds; Video B clipped via SetWindowRgn
            Grid.SetColumn(PlayerAControl, 0);
            Grid.SetColumnSpan(PlayerAControl, 3);
            Grid.SetColumn(PlayerBControl, 0);
            Grid.SetColumnSpan(PlayerBControl, 3);

            ColVideoA.Width = new GridLength(1, GridUnitType.Star);
            ColVideoSplitter.Width = new GridLength(0);
            ColVideoB.Width = new GridLength(0);
            CompareSplitter.Visibility = Visibility.Collapsed;

            PlayerAControl.Width = double.NaN;
            PlayerAControl.HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch;
            PlayerAControl.VerticalAlignment = System.Windows.VerticalAlignment.Stretch;
            PlayerAControl.Visibility = Visibility.Visible;
            PlayerAControl.ResetClip();

            PlayerBControl.Width = double.NaN;
            PlayerBControl.HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch;
            PlayerBControl.VerticalAlignment = System.Windows.VerticalAlignment.Stretch;

            PlayerBControl.SetWipeClip(_vm.OverlayOpacity);
        }
        else
        {
            // SIDE BY SIDE MODE: 50% / 50% split with draggable splitter
            Grid.SetColumn(PlayerAControl, 0);
            Grid.SetColumnSpan(PlayerAControl, 1);
            Grid.SetColumn(PlayerBControl, 2);
            Grid.SetColumnSpan(PlayerBControl, 1);

            ColVideoA.Width = new GridLength(1, GridUnitType.Star);
            ColVideoSplitter.Width = GridLength.Auto;
            ColVideoB.Width = new GridLength(1, GridUnitType.Star);
            CompareSplitter.Visibility = Visibility.Visible;

            PlayerAControl.Width = double.NaN;
            PlayerAControl.HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch;
            PlayerAControl.VerticalAlignment = System.Windows.VerticalAlignment.Stretch;
            PlayerAControl.ResetClip();

            PlayerBControl.Width = double.NaN;
            PlayerBControl.HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch;
            PlayerBControl.VerticalAlignment = System.Windows.VerticalAlignment.Stretch;
            PlayerBControl.ResetClip();

            PlayerBControl.Visibility = Visibility.Visible;
        }
    }

    private void OnWindowClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _settingsService.Save();
        _coordinator.Dispose();
        _waveformService.CancelCurrent();
    }

    private void OnWindowPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        // Don't intercept typing in textboxes
        if (e.OriginalSource is TextBox) return;

        bool isShift = (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift;
        bool isCtrl = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;

        // Quick A/B Flip for Overlay mode
        if (e.Key == Key.Tab && _vm.IsOverlay)
        {
            PlayerBControl.SetWindowOpacity(0.0);
            e.Handled = true;
            return;
        }

        if (isCtrl && isShift && e.Key == Key.O)
        {
            _vm.OpenFileBCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (isCtrl && e.Key == Key.O)
        {
            _vm.OpenFileACommand.Execute(null);
            e.Handled = true;
            return;
        }

        switch (e.Key)
        {
            case Key.Space:
                _vm.PlayPauseCommand.Execute(null);
                e.Handled = true;
                break;

            case Key.Left:
                if (isShift)
                {
                    _vm.SeekRelativeCommand.Execute("-1");
                }
                else
                {
                    _vm.FrameStepBackwardCommand.Execute(null);
                }
                e.Handled = true;
                break;

            case Key.Right:
                if (isShift)
                {
                    _vm.SeekRelativeCommand.Execute("1");
                }
                else
                {
                    _vm.FrameStepForwardCommand.Execute(null);
                }
                e.Handled = true;
                break;

            case Key.F:
                _vm.ToggleFullscreenCommand.Execute(null);
                e.Handled = true;
                break;

            case Key.Escape:
                if (_vm.IsFullscreen)
                {
                    _vm.IsFullscreen = false;
                    e.Handled = true;
                }
                break;

            case Key.W:
                _vm.ToggleWaveformCommand.Execute(null);
                e.Handled = true;
                break;

            case Key.M:
                _vm.IsMuted = !_vm.IsMuted;
                e.Handled = true;
                break;

            case Key.OemOpenBrackets: // [
                _vm.SetLoopACommand.Execute(null);
                e.Handled = true;
                break;

            case Key.OemCloseBrackets: // ]
                _vm.SetLoopBCommand.Execute(null);
                e.Handled = true;
                break;

            case Key.OemBackslash:
                _vm.ClearLoopCommand.Execute(null);
                e.Handled = true;
                break;
        }
    }

    private void OnWindowPreviewKeyUp(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Tab && _vm.IsOverlay)
        {
            PlayerBControl.SetWindowOpacity(_vm.OverlayOpacity);
            e.Handled = true;
        }
    }

    private void OnFlipMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_vm.IsOverlay)
        {
            PlayerBControl.SetWindowOpacity(0.0);
        }
    }

    private void OnFlipMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_vm.IsOverlay)
        {
            PlayerBControl.SetWindowOpacity(_vm.OverlayOpacity);
        }
    }

    private void ApplyFullscreen(bool fullscreen)
    {
        if (fullscreen)
        {
            _previousWindowStyle = WindowStyle;
            _previousWindowState = WindowState;

            WindowStyle = WindowStyle.None;
            WindowState = WindowState.Normal;
            WindowState = WindowState.Maximized;
            Topmost = true;
        }
        else
        {
            Topmost = false;
            WindowStyle = _previousWindowStyle;
            WindowState = _previousWindowState;
        }
    }

    private void OnTimelinePreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed && sender is FrameworkElement elem)
        {
            _isSliderDragging = true;
            elem.CaptureMouse();
            ProcessTimelineSeek(elem, e.GetPosition(elem).X);
            e.Handled = true;
        }
    }

    private void OnTimelinePreviewMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_isSliderDragging && e.LeftButton == MouseButtonState.Pressed && sender is FrameworkElement elem)
        {
            ProcessTimelineSeek(elem, e.GetPosition(elem).X);
            e.Handled = true;
        }
    }

    private void OnTimelinePreviewMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_isSliderDragging)
        {
            _isSliderDragging = false;
            if (sender is FrameworkElement elem)
            {
                elem.ReleaseMouseCapture();
            }
            e.Handled = true;
        }
    }

    private void ProcessTimelineSeek(FrameworkElement elem, double mouseX)
    {
        double duration = _vm.Duration > 0 ? _vm.Duration : (TimelineSlider.Maximum > 0 ? TimelineSlider.Maximum : 0);
        if (duration <= 0 || elem.ActualWidth <= 0) return;

        double fraction = Math.Clamp(mouseX / elem.ActualWidth, 0.0, 1.0);
        double targetSec = fraction * duration;
        _vm.UserSeek(targetSec);
    }

    private void OnWaveformSeekRequested(double targetSeconds)
    {
        _vm.UserSeek(targetSeconds);
    }

    private void OnJumpTimecodeClick(object sender, RoutedEventArgs e)
    {
        var dlg = new TimecodeJumpDialog(_vm.TimecodeA) { Owner = this };
        if (dlg.ShowDialog() == true)
        {
            _vm.JumpToTimecodeOrFrame(dlg.ResultText);
        }
    }

    private void OnShowMetadataClick(object sender, RoutedEventArgs e)
    {
        var dlg = new MetadataDialog(_vm.MetadataA, _vm.MetadataB) { Owner = this };
        dlg.ShowDialog();
    }

    private void OnAudioSourceAClick(object sender, RoutedEventArgs e)
    {
        _vm.SelectedAudioSource = AudioSourceOption.AudioA;
    }

    private void OnAudioSourceBClick(object sender, RoutedEventArgs e)
    {
        _vm.SelectedAudioSource = AudioSourceOption.AudioB;
    }

    private void OnAudioSourceMuteClick(object sender, RoutedEventArgs e)
    {
        _vm.SelectedAudioSource = AudioSourceOption.Mute;
    }

    private void OnPlaylistDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (_vm.SelectedPlaylistItem != null)
        {
            _vm.LoadVideoA(_vm.SelectedPlaylistItem.FilePath);
        }
    }

    private void OnPlaylistPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        _playlistDragStartPoint = e.GetPosition(null);
    }

    private void OnPlaylistPreviewMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed && sender is ListBox lb && lb.SelectedItem is MediaItem item)
        {
            Point currentPos = e.GetPosition(null);
            Vector diff = _playlistDragStartPoint - currentPos;
            if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
                Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
            {
                DragDrop.DoDragDrop(lb, item.FilePath, DragDropEffects.Copy);
            }
        }
    }

    private void OnPlaylistDragOver(object sender, System.Windows.DragEventArgs e)
    {
        if (e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop))
        {
            e.Effects = System.Windows.DragDropEffects.Copy;
        }
        else
        {
            e.Effects = System.Windows.DragDropEffects.None;
        }
        e.Handled = true;
    }

    private void OnPlaylistDrop(object sender, System.Windows.DragEventArgs e)
    {
        if (e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop))
        {
            var files = (string[])e.Data.GetData(System.Windows.DataFormats.FileDrop);
            if (files != null)
            {
                foreach (var file in files)
                {
                    if (File.Exists(file) && !_vm.Playlist.Any(p => p.FilePath.Equals(file, StringComparison.OrdinalIgnoreCase)))
                    {
                        _vm.Playlist.Add(new MediaItem(file));
                    }
                }
            }
        }
    }
}

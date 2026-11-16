using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace WindowsVideoPreview.Views;

public partial class WaveformControl : System.Windows.Controls.UserControl
{
    private bool _isDragging;
    private readonly System.Windows.Media.Brush _playedTopBrush;
    private readonly System.Windows.Media.Brush _playedBottomBrush;
    private readonly System.Windows.Media.Brush _unplayedTopBrush;
    private readonly System.Windows.Media.Brush _unplayedBottomBrush;
    private readonly System.Windows.Media.Brush _centerLineBrush;

    public static readonly DependencyProperty WaveformDataProperty =
        DependencyProperty.Register(nameof(WaveformData), typeof(float[]), typeof(WaveformControl),
            new PropertyMetadata(Array.Empty<float>(), OnWaveformDataChanged));

    public static readonly DependencyProperty PositionProperty =
        DependencyProperty.Register(nameof(Position), typeof(double), typeof(WaveformControl),
            new PropertyMetadata(0.0, OnPositionChanged));

    public static readonly DependencyProperty DurationProperty =
        DependencyProperty.Register(nameof(Duration), typeof(double), typeof(WaveformControl),
            new PropertyMetadata(0.0, OnDurationChanged));

    public static readonly DependencyProperty LoopStartProperty =
        DependencyProperty.Register(nameof(LoopStart), typeof(double?), typeof(WaveformControl),
            new PropertyMetadata(null, OnLoopChanged));

    public static readonly DependencyProperty LoopEndProperty =
        DependencyProperty.Register(nameof(LoopEnd), typeof(double?), typeof(WaveformControl),
            new PropertyMetadata(null, OnLoopChanged));

    public event Action<double>? SeekRequested;

    // Set to false when the player is in EOF/ended state to prevent crash-triggering seeks
    public bool IsSeekEnabled { get; set; } = true;

    public float[] WaveformData
    {
        get => (float[])GetValue(WaveformDataProperty);
        set => SetValue(WaveformDataProperty, value);
    }

    public double Position
    {
        get => (double)GetValue(PositionProperty);
        set => SetValue(PositionProperty, value);
    }

    public double Duration
    {
        get => (double)GetValue(DurationProperty);
        set => SetValue(DurationProperty, value);
    }

    public double? LoopStart
    {
        get => (double?)GetValue(LoopStartProperty);
        set => SetValue(LoopStartProperty, value);
    }

    public double? LoopEnd
    {
        get => (double?)GetValue(LoopEndProperty);
        set => SetValue(LoopEndProperty, value);
    }

    public WaveformControl()
    {
        InitializeComponent();

        _playedTopBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(56, 189, 248)); // Sky 400
        _playedTopBrush.Freeze();
        _playedBottomBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(2, 132, 199)); // Sky 600
        _playedBottomBrush.Freeze();

        _unplayedTopBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(82, 82, 91)); // Zinc 600
        _unplayedTopBrush.Freeze();
        _unplayedBottomBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(63, 63, 70)); // Zinc 700
        _unplayedBottomBrush.Freeze();

        _centerLineBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(40, 255, 255, 255));
        _centerLineBrush.Freeze();

        SizeChanged += (_, _) => Redraw();
    }

    private static void OnWaveformDataChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is WaveformControl ctrl) ctrl.Redraw();
    }

    private static void OnPositionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is WaveformControl ctrl) ctrl.UpdatePlayhead();
    }

    private static void OnDurationChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is WaveformControl ctrl) ctrl.Redraw();
    }

    private static void OnLoopChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is WaveformControl ctrl) ctrl.UpdateLoopRect();
    }

    private void Redraw()
    {
        WaveformCanvas.Children.Clear();
        TicksCanvas.Children.Clear();

        double w = ActualWidth;
        double h = ActualHeight;
        if (w <= 0 || h <= 0) return;

        PlayheadLine.Y2 = h;
        HoverGuideLine.Y2 = h;

        var data = WaveformData;
        if (data == null || data.Length == 0)
        {
            NoAudioNotice.Visibility = Visibility.Visible;
            DrawCenterLine(w, h);
            UpdatePlayhead();
            UpdateLoopRect();
            return;
        }

        NoAudioNotice.Visibility = Visibility.Collapsed;

        // Draw symmetric mirrored waveform
        int bars = Math.Max(20, (int)(w / 2.2)); // 1 bar per 2.2 px
        double step = (double)data.Length / bars;
        double barWidth = Math.Max(1.2, (w / bars) - 0.4);
        double midY = h / 2.0;
        double maxHalfHeight = (h / 2.0) - 6.0;

        double playheadFraction = (Duration > 0) ? Math.Clamp(Position / Duration, 0.0, 1.0) : 0.0;
        double playheadX = playheadFraction * w;

        for (int i = 0; i < bars; i++)
        {
            int startIdx = (int)(i * step);
            int endIdx = Math.Min(data.Length, (int)((i + 1) * step));
            float maxVal = 0f;
            for (int j = startIdx; j < endIdx; j++)
            {
                if (data[j] > maxVal) maxVal = data[j];
            }

            double x = (i / (double)bars) * w;
            bool isPlayed = x <= playheadX;

            // Minimum height of 2px for noise/silence, scale up clearly for audible signal
            double peakHeight = Math.Max(2.0, maxVal * maxHalfHeight);
            double bottomHeight = peakHeight * 0.75;

            // Upper bar
            var topRect = new System.Windows.Shapes.Rectangle
            {
                Width = barWidth,
                Height = peakHeight,
                Fill = isPlayed ? _playedTopBrush : _unplayedTopBrush,
                RadiusX = 1,
                RadiusY = 1,
                Tag = isPlayed ? "played" : "unplayed"
            };
            Canvas.SetLeft(topRect, x);
            Canvas.SetTop(topRect, midY - peakHeight);
            WaveformCanvas.Children.Add(topRect);

            // Lower mirrored bar
            var bottomRect = new System.Windows.Shapes.Rectangle
            {
                Width = barWidth,
                Height = bottomHeight,
                Fill = isPlayed ? _playedBottomBrush : _unplayedBottomBrush,
                RadiusX = 1,
                RadiusY = 1,
                Tag = isPlayed ? "played" : "unplayed"
            };
            Canvas.SetLeft(bottomRect, x);
            Canvas.SetTop(bottomRect, midY);
            WaveformCanvas.Children.Add(bottomRect);
        }

        DrawCenterLine(w, h);
        DrawTimeTicks(w, h);
        UpdatePlayhead();
        UpdateLoopRect();
    }

    private void DrawCenterLine(double w, double h)
    {
        var line = new Line
        {
            X1 = 0,
            X2 = w,
            Y1 = h / 2.0,
            Y2 = h / 2.0,
            Stroke = _centerLineBrush,
            StrokeThickness = 1
        };
        WaveformCanvas.Children.Add(line);
    }

    private void DrawTimeTicks(double w, double h)
    {
        if (Duration <= 0 || w <= 0) return;

        double tickInterval;
        if (Duration <= 15) tickInterval = 1.0;
        else if (Duration <= 60) tickInterval = 5.0;
        else if (Duration <= 300) tickInterval = 15.0;
        else if (Duration <= 1200) tickInterval = 60.0;
        else tickInterval = 120.0;

        for (double t = tickInterval; t < Duration; t += tickInterval)
        {
            double x = (t / Duration) * w;

            // Tick line
            var tick = new Line
            {
                X1 = x,
                X2 = x,
                Y1 = h - 6,
                Y2 = h,
                Stroke = new SolidColorBrush(System.Windows.Media.Color.FromArgb(90, 255, 255, 255)),
                StrokeThickness = 1
            };
            TicksCanvas.Children.Add(tick);

            // Time label text
            if (w / (Duration / tickInterval) > 40)
            {
                var ts = TimeSpan.FromSeconds(t);
                string text = ts.TotalHours >= 1 ? ts.ToString(@"hh\:mm\:ss") : ts.ToString(@"mm\:ss");
                var tb = new TextBlock
                {
                    Text = text,
                    FontSize = 9,
                    Foreground = new SolidColorBrush(System.Windows.Media.Color.FromArgb(120, 255, 255, 255)),
                    FontFamily = new System.Windows.Media.FontFamily("Consolas, Segoe UI")
                };
                Canvas.SetLeft(tb, x + 2);
                Canvas.SetTop(tb, h - 14);
                TicksCanvas.Children.Add(tb);
            }
        }
    }

    private void UpdatePlayhead()
    {
        if (Duration <= 0 || ActualWidth <= 0)
        {
            PlayheadLine.Visibility = Visibility.Collapsed;
            PlayheadIndicator.Visibility = Visibility.Collapsed;
            return;
        }

        double fraction = Math.Clamp(Position / Duration, 0.0, 1.0);
        double x = fraction * ActualWidth;

        PlayheadLine.X1 = x;
        PlayheadLine.X2 = x;
        PlayheadLine.Visibility = Visibility.Visible;

        Canvas.SetLeft(PlayheadIndicator, Math.Max(0, x - 4));
        PlayheadIndicator.Visibility = Visibility.Visible;

        // Efficiently update bar colors based on new playhead position
        foreach (UIElement child in WaveformCanvas.Children)
        {
            if (child is System.Windows.Shapes.Rectangle rect && rect.Tag is string tag)
            {
                double rectX = Canvas.GetLeft(rect);
                bool isPlayed = rectX <= x;
                bool isTop = Canvas.GetTop(rect) < (ActualHeight / 2.0);

                if (isPlayed && tag == "unplayed")
                {
                    rect.Fill = isTop ? _playedTopBrush : _playedBottomBrush;
                    rect.Tag = "played";
                }
                else if (!isPlayed && tag == "played")
                {
                    rect.Fill = isTop ? _unplayedTopBrush : _unplayedBottomBrush;
                    rect.Tag = "unplayed";
                }
            }
        }
    }

    private void UpdateLoopRect()
    {
        if (LoopBand == null || LoopInMarker == null || LoopOutMarker == null) return;

        if (Duration <= 0 || ActualWidth <= 0 || (!LoopStart.HasValue && !LoopEnd.HasValue))
        {
            LoopBand.Visibility = Visibility.Collapsed;
            LoopInMarker.Visibility = Visibility.Collapsed;
            LoopOutMarker.Visibility = Visibility.Collapsed;
            return;
        }

        double start = LoopStart ?? 0.0;
        double end = LoopEnd ?? Duration;

        if (end <= start)
        {
            LoopBand.Visibility = Visibility.Collapsed;
            LoopInMarker.Visibility = Visibility.Collapsed;
            LoopOutMarker.Visibility = Visibility.Collapsed;
            return;
        }

        double startX = Math.Clamp(start / Duration, 0.0, 1.0) * ActualWidth;
        double endX = Math.Clamp(end / Duration, 0.0, 1.0) * ActualWidth;
        double h = ActualHeight > 0 ? ActualHeight : 64;

        Canvas.SetLeft(LoopBand, startX);
        LoopBand.Width = Math.Max(2, endX - startX);
        LoopBand.Height = h;
        LoopBand.Visibility = Visibility.Visible;

        Canvas.SetLeft(LoopInMarker, Math.Max(0, startX - 1));
        LoopInMarker.Height = h;
        LoopInMarker.Visibility = LoopStart.HasValue ? Visibility.Visible : Visibility.Collapsed;

        Canvas.SetLeft(LoopOutMarker, Math.Max(0, endX - 2));
        LoopOutMarker.Height = h;
        LoopOutMarker.Visibility = LoopEnd.HasValue ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            _isDragging = true;
            CaptureMouse();
            ProcessMouseSeek(e.GetPosition(this).X);
        }
    }

    private void OnMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        double x = e.GetPosition(this).X;
        if (Duration > 0 && ActualWidth > 0)
        {
            double frac = Math.Clamp(x / ActualWidth, 0.0, 1.0);
            var hoverTs = TimeSpan.FromSeconds(frac * Duration);
            HoverTimeText.Text = $"{(int)hoverTs.TotalMinutes:D2}:{hoverTs.Seconds:D2}.{hoverTs.Milliseconds:D3}";

            HoverGuideLine.X1 = x;
            HoverGuideLine.X2 = x;
            HoverGuideLine.Visibility = Visibility.Visible;

            double pillX = Math.Clamp(x - 30, 4, ActualWidth - 75);
            Canvas.SetLeft(HoverPill, pillX);
            HoverPill.Visibility = Visibility.Visible;
        }

        if (_isDragging && e.LeftButton == MouseButtonState.Pressed)
        {
            ProcessMouseSeek(x);
        }
    }

    private void OnMouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        HoverGuideLine.Visibility = Visibility.Collapsed;
        HoverPill.Visibility = Visibility.Collapsed;
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_isDragging)
        {
            _isDragging = false;
            ReleaseMouseCapture();
        }
    }

    private void ProcessMouseSeek(double mouseX)
    {
        if (!IsSeekEnabled || Duration <= 0 || ActualWidth <= 0) return;
        double fraction = Math.Clamp(mouseX / ActualWidth, 0.0, 1.0);
        double targetSec = fraction * Duration;
        SeekRequested?.Invoke(targetSec);
    }
}

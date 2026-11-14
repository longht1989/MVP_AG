using System;
using System.Diagnostics;
using System.Globalization;
using Nickvision.MPVSharp;

namespace WindowsVideoPreview.Services;

public class MpvPlayer : IDisposable
{
    private Client? _client;
    private bool _disposed;
    private readonly object _lock = new();

    public event Action<double>? TimePosChanged;
    public event Action<double>? DurationChanged;
    public event Action<bool>? PauseChanged;
    public event Action<double>? FpsChanged;
    public event Action? FileLoaded;
    public event Action? FileEnded;

    public double TimePos { get; private set; }
    public double Duration { get; private set; }
    public bool IsPaused { get; private set; } = true;
    public double Fps { get; private set; }
    public long CurrentFrame => Fps > 0 ? (long)Math.Floor(TimePos * Fps) : 0;
    public long TotalFrames => (Fps > 0 && Duration > 0) ? (long)Math.Round(Duration * Fps) : 0;
    public bool IsLoaded { get; private set; }
    public bool IsEnded { get; private set; }
    public string CurrentFilePath { get; private set; } = string.Empty;

    public void Initialize(IntPtr hwnd)
    {
        lock (_lock)
        {
            if (_client != null) return;

            _client = new Client();
            if (hwnd != IntPtr.Zero)
            {
                SafeSetProperty("vo", "gpu");
                SafeSetProperty("wid", hwnd.ToInt64());
                SafeSetProperty("gpu-api", "d3d11");
                SafeSetProperty("gpu-context", "d3d11");
                SafeSetProperty("d3d11-flip", "no");
            }
            else
            {
                SafeSetProperty("vo", "null");
            }

            SafeSetProperty("keep-open", "always");

            _client.Initialize();

            _client.FileLoaded += OnFileLoaded;
            _client.FileEnded += OnFileEnded;
            _client.PropertyChanged += OnPropertyChanged;

            _client.ObserveProperty("time-pos", 1);
            _client.ObserveProperty("duration", 2);
            _client.ObserveProperty("pause", 3);
            _client.ObserveProperty("estimated-vf-fps", 4);
        }
    }

    private void SafeSetProperty(string name, string value)
    {
        try
        {
            _client?.SetProperty(name, value);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MpvPlayer] SafeSetProperty failed for '{name}'='{value}': {ex.Message}");
        }
    }

    private void SafeSetProperty(string name, long value)
    {
        try
        {
            _client?.SetProperty(name, value);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MpvPlayer] SafeSetProperty failed for '{name}'={value}: {ex.Message}");
        }
    }

    private void OnFileLoaded()
    {
        IsLoaded = true;
        IsEnded = false;
        try
        {
            if (_client != null)
            {
                try
                {
                    _client.GetProperty("duration", out double dur);
                    Duration = dur;
                    DurationChanged?.Invoke(dur);
                }
                catch { }

                try
                {
                    _client.GetProperty("estimated-vf-fps", out double fps);
                    if (fps > 0)
                    {
                        Fps = fps;
                        FpsChanged?.Invoke(fps);
                    }
                }
                catch { }
            }
        }
        catch { }

        FileLoaded?.Invoke();
    }

    private void OnFileEnded(object? sender, EventArgs e)
    {
        IsEnded = true;
        FileEnded?.Invoke();
    }

    private void OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_client == null) return;

        try
        {
            switch (e.Name)
            {
                case "time-pos":
                    try
                    {
                        _client.GetProperty("time-pos", out double timePos);
                        TimePos = timePos;
                        TimePosChanged?.Invoke(timePos);
                    }
                    catch { }
                    break;
                case "duration":
                    try
                    {
                        _client.GetProperty("duration", out double duration);
                        Duration = duration;
                        DurationChanged?.Invoke(duration);
                    }
                    catch { }
                    break;
                case "pause":
                    try
                    {
                        _client.GetProperty("pause", out bool paused);
                        IsPaused = paused;
                        PauseChanged?.Invoke(paused);
                    }
                    catch { }
                    break;
                case "estimated-vf-fps":
                    try
                    {
                        _client.GetProperty("estimated-vf-fps", out double fps);
                        if (fps > 0)
                        {
                            Fps = fps;
                            FpsChanged?.Invoke(fps);
                        }
                    }
                    catch { }
                    break;
            }
        }
        catch { }
    }

    public void Load(string filePath)
    {
        lock (_lock)
        {
            if (_client == null) return;
            CurrentFilePath = filePath;
            IsLoaded = false;
            IsEnded = false;
            _client.CommandString($"loadfile \"{filePath.Replace("\\", "/")}\" replace");
        }
    }

    public void Play()
    {
        lock (_lock)
        {
            if (_client == null) return;
            _client.SetProperty("pause", false);
            IsPaused = false;
        }
    }

    public void Pause()
    {
        lock (_lock)
        {
            if (_client == null) return;
            _client.SetProperty("pause", true);
            IsPaused = true;
        }
    }

    public void TogglePause()
    {
        lock (_lock)
        {
            if (_client == null) return;
            if (IsPaused) Play();
            else Pause();
        }
    }

    public void Seek(double seconds, bool exact = true)
    {
        lock (_lock)
        {
            if (_client == null || !IsLoaded) return;
            try
            {
                var secStr = seconds.ToString(CultureInfo.InvariantCulture);
                var flag = exact ? "absolute+exact" : "absolute";
                _client.CommandString($"seek {secStr} {flag}");
                TimePos = Math.Max(0, seconds);
                IsEnded = false;
            }
            catch (Exception ex)
            {
                // Silently ignore command errors when seeking (e.g. at EOF boundary or unloaded state)
                Debug.WriteLine($"[MpvPlayer] Seek failed: {ex.Message}");
            }
        }
    }

    public void SeekRelative(double deltaSeconds)
    {
        lock (_lock)
        {
            if (_client == null || !IsLoaded) return;
            try
            {
                var secStr = deltaSeconds.ToString(CultureInfo.InvariantCulture);
                _client.CommandString($"seek {secStr} relative+exact");
                IsEnded = false;
            }
            catch (Exception ex)
            {
                // Silently ignore command errors
                Debug.WriteLine($"[MpvPlayer] SeekRelative failed: {ex.Message}");
            }
        }
    }

    public void FrameStep()
    {
        lock (_lock)
        {
            if (_client == null || !IsLoaded) return;
            try
            {
                Pause();
                _client.CommandString("frame-step");
                IsEnded = false;
                try
                {
                    _client.GetProperty("time-pos", out double pos);
                    TimePos = pos;
                    TimePosChanged?.Invoke(pos);
                }
                catch { }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MpvPlayer] FrameStep failed: {ex.Message}");
            }
        }
    }

    public void FrameBackStep()
    {
        lock (_lock)
        {
            if (_client == null || !IsLoaded) return;
            try
            {
                Pause();
                _client.CommandString("frame-back-step");
                IsEnded = false;
                try
                {
                    _client.GetProperty("time-pos", out double pos);
                    TimePos = pos;
                    TimePosChanged?.Invoke(pos);
                }
                catch { }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MpvPlayer] FrameBackStep failed: {ex.Message}");
            }
        }
    }

    public void SetSpeed(double speed)
    {
        lock (_lock)
        {
            if (_client == null) return;
            _client.SetProperty("speed", Math.Clamp(speed, 0.1, 8.0));
        }
    }

    public void SetVolume(double volume)
    {
        lock (_lock)
        {
            if (_client == null) return;
            _client.SetProperty("volume", Math.Clamp(volume, 0.0, 100.0));
        }
    }

    public void SetMute(bool mute)
    {
        lock (_lock)
        {
            if (_client == null) return;
            _client.SetProperty("mute", mute);
        }
    }

    public void SetLoopRange(double? start, double? end)
    {
        lock (_lock)
        {
            if (_client == null) return;
            if (start.HasValue)
                _client.SetProperty("ab-loop-a", start.Value.ToString(CultureInfo.InvariantCulture));
            else
                _client.SetProperty("ab-loop-a", "no");

            if (end.HasValue)
                _client.SetProperty("ab-loop-b", end.Value.ToString(CultureInfo.InvariantCulture));
            else
                _client.SetProperty("ab-loop-b", "no");
        }
    }

    public void SetProperty(string name, string value)
    {
        lock (_lock)
        {
            _client?.SetProperty(name, value);
        }
    }

    public void SetProperty(string name, double value)
    {
        lock (_lock)
        {
            _client?.SetProperty(name, value);
        }
    }

    public void SetProperty(string name, bool value)
    {
        lock (_lock)
        {
            _client?.SetProperty(name, value);
        }
    }

    public void SetProperty(string name, long value)
    {
        lock (_lock)
        {
            _client?.SetProperty(name, value);
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;
            try
            {
                _client?.Dispose();
            }
            catch { }
            _client = null;
        }
    }
}

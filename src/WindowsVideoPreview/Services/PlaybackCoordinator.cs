using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace WindowsVideoPreview.Services;

public enum PlaybackMode
{
    Single,
    Compare
}

public enum AudioSourceOption
{
    AudioA,
    AudioB,
    Mute
}

public enum PlaybackLoopMode
{
    NoLoop,
    Loop
}

public class PlaybackCoordinator : IDisposable
{
    private readonly MpvPlayer _playerA;
    private readonly MpvPlayer _playerB;
    private System.Threading.Timer? _syncTimer;
    private bool _isSyncing;
    private bool _disposed;

    public PlaybackMode Mode { get; set; } = PlaybackMode.Single;
    public AudioSourceOption AudioSource { get; set; } = AudioSourceOption.AudioA;
    public PlaybackLoopMode LoopMode { get; set; } = PlaybackLoopMode.NoLoop;
    public double OffsetSeconds { get; set; } = 0.0;
    public double MasterVolume { get; set; } = 100.0;
    public bool IsMuted { get; set; } = false;
    public double PlaybackSpeed { get; set; } = 1.0;

    public double? LoopStart { get; set; }
    public double? LoopEnd { get; set; }

    public event Action<double>? MasterPositionChanged;
    public event Action<bool>? PlayStateChanged;

    public MpvPlayer PlayerA => _playerA;
    public MpvPlayer PlayerB => _playerB;

    public double CurrentMasterPosition => _playerA.TimePos;
    public double CurrentMasterDuration => _playerA.Duration;
    public bool IsPlaying => !_playerA.IsPaused;

    public PlaybackCoordinator(MpvPlayer playerA, MpvPlayer playerB)
    {
        _playerA = playerA;
        _playerB = playerB;

        _playerA.TimePosChanged += OnPlayerATimePosChanged;
        _playerA.PauseChanged += OnPlayerAPauseChanged;
        _playerA.FileEnded += OnPlayerAFileEnded;

        // Periodic drift checker for compare mode
        _syncTimer = new System.Threading.Timer(OnSyncTick, null, 250, 250);
    }

    private void OnPlayerAFileEnded()
    {
        if (LoopMode == PlaybackLoopMode.Loop)
        {
            double restartPos = LoopStart ?? 0.0;
            _playerA.SetPlayDirection(true);
            Seek(restartPos, exact: true);
            Play();
        }
        else
        {
            Pause();
        }
    }

    private void OnPlayerATimePosChanged(double pos)
    {
        bool hasLoopAB = LoopStart.HasValue && LoopEnd.HasValue && LoopEnd.Value > LoopStart.Value;
        double lowerBound = hasLoopAB ? LoopStart!.Value : 0.0;
        double upperBound = hasLoopAB ? LoopEnd!.Value : _playerA.Duration;

        if (upperBound > lowerBound && _playerA.IsLoaded)
        {
            if (pos >= upperBound)
            {
                if (LoopMode == PlaybackLoopMode.Loop)
                {
                    Seek(lowerBound, exact: true);
                    return;
                }
                else // Once (NoLoop)
                {
                    Pause();
                    Seek(upperBound, exact: true);
                    return;
                }
            }
        }

        MasterPositionChanged?.Invoke(pos);
    }

    private void OnPlayerAPauseChanged(bool isPaused)
    {
        PlayStateChanged?.Invoke(!isPaused);
        if (Mode == PlaybackMode.Compare && _playerB.IsLoaded)
        {
            if (isPaused)
            {
                _playerB.Pause();
            }
            else
            {
                ApplyAudioRouting();
                _playerB.Play();
            }
        }
    }

    private void OnSyncTick(object? state)
    {
        if (_disposed || Mode != PlaybackMode.Compare || _isSyncing) return;
        if (!_playerA.IsLoaded || !_playerB.IsLoaded) return;
        if (_playerA.IsPaused) return;

        try
        {
            _isSyncing = true;
            double targetB = _playerA.TimePos - OffsetSeconds;
            if (targetB < 0 || targetB > _playerB.Duration)
            {
                if (!_playerB.IsPaused) _playerB.Pause();
                return;
            }

            if (_playerB.IsPaused && !_playerA.IsPaused)
            {
                _playerB.Seek(targetB, exact: false);
                _playerB.Play();
                return;
            }

            double drift = Math.Abs(_playerB.TimePos - targetB);
            // If drift exceeds 60ms, resync Player B
            if (drift > 0.06)
            {
                _playerB.Seek(targetB, exact: false);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Sync tick error: {ex.Message}");
        }
        finally
        {
            _isSyncing = false;
        }
    }

    public void Play()
    {
        // Replay at EOF or Loop boundary
        if (_playerA.IsEnded || (_playerA.Duration > 0 && _playerA.TimePos >= _playerA.Duration - 0.08))
        {
            double restartPos = LoopStart ?? 0.0;
            _playerA.SetPlayDirection(true);
            Seek(restartPos, exact: true);
        }
        else if (LoopEnd.HasValue && LoopStart.HasValue && _playerA.TimePos >= LoopEnd.Value)
        {
            _playerA.SetPlayDirection(true);
            Seek(LoopStart.Value, exact: true);
        }

        ApplyAudioRouting();
        _playerA.Play();
        if (Mode == PlaybackMode.Compare && _playerB.IsLoaded)
        {
            double targetB = _playerA.TimePos - OffsetSeconds;
            if (targetB >= 0 && targetB <= _playerB.Duration)
            {
                _playerB.Seek(targetB, exact: false);
                _playerB.Play();
            }
            else
            {
                _playerB.Pause();
            }
        }
    }

    public void Pause()
    {
        _playerA.Pause();
        if (Mode == PlaybackMode.Compare && _playerB.IsLoaded)
        {
            _playerB.Pause();
        }
    }

    public void TogglePlayPause()
    {
        if (_playerA.IsEnded || (_playerA.Duration > 0 && _playerA.TimePos >= _playerA.Duration - 0.08))
        {
            double restartPos = LoopStart ?? 0.0;
            _playerA.SetPlayDirection(true);
            Seek(restartPos, exact: true);
            Play();
            return;
        }

        if (IsPlaying) Pause();
        else Play();
    }

    public void Seek(double targetSeconds, bool exact = true)
    {
        _playerA.SetPlayDirection(true);
        _playerA.Seek(targetSeconds, exact);
        if (Mode == PlaybackMode.Compare && _playerB.IsLoaded)
        {
            double targetB = targetSeconds - OffsetSeconds;
            if (targetB >= 0)
            {
                _playerB.Seek(targetB, exact);
            }
            else
            {
                _playerB.Seek(0, exact);
            }
        }
    }

    public void SeekRelative(double deltaSeconds)
    {
        Seek(_playerA.TimePos + deltaSeconds);
    }

    public void StepFrameForward()
    {
        Pause();
        _playerA.FrameStep();
        if (Mode == PlaybackMode.Compare && _playerB.IsLoaded)
        {
            double targetB = _playerA.TimePos - OffsetSeconds;
            _playerB.Seek(Math.Max(0, targetB), exact: true);
        }
    }

    public void StepFrameBackward()
    {
        Pause();
        _playerA.FrameBackStep();
        if (Mode == PlaybackMode.Compare && _playerB.IsLoaded)
        {
            double targetB = _playerA.TimePos - OffsetSeconds;
            _playerB.Seek(Math.Max(0, targetB), exact: true);
        }
    }

    public void SetSpeed(double speed)
    {
        PlaybackSpeed = speed;
        _playerA.SetSpeed(speed);
        if (_playerB.IsLoaded)
        {
            _playerB.SetSpeed(speed);
        }
    }

    public void SetVolume(double volume)
    {
        MasterVolume = volume;
        ApplyAudioRouting();
    }

    public void SetMute(bool mute)
    {
        IsMuted = mute;
        ApplyAudioRouting();
    }

    public void SetAudioSource(AudioSourceOption source)
    {
        AudioSource = source;
        ApplyAudioRouting();
    }

    public void ApplyAudioRouting()
    {
        if (IsMuted || AudioSource == AudioSourceOption.Mute)
        {
            _playerA.SetMute(true);
            _playerB.SetMute(true);
            return;
        }

        if (Mode == PlaybackMode.Single)
        {
            _playerA.SetMute(false);
            _playerA.SetVolume(MasterVolume);
            _playerB.SetMute(true);
            return;
        }

        // Compare mode
        if (AudioSource == AudioSourceOption.AudioA)
        {
            _playerA.SetMute(false);
            _playerA.SetVolume(MasterVolume);
            _playerB.SetMute(true);
        }
        else if (AudioSource == AudioSourceOption.AudioB)
        {
            _playerA.SetMute(true);
            _playerB.SetMute(false);
            _playerB.SetVolume(MasterVolume);
        }
    }

    public void UpdateOffset(double newOffset)
    {
        OffsetSeconds = newOffset;
        if (Mode == PlaybackMode.Compare && _playerB.IsLoaded)
        {
            double targetB = _playerA.TimePos - OffsetSeconds;
            _playerB.Seek(Math.Max(0, targetB), exact: true);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _syncTimer?.Dispose();
        _syncTimer = null;
        _playerA.Dispose();
        _playerB.Dispose();
    }
}

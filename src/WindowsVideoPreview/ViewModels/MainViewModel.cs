using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using WindowsVideoPreview.Models;
using WindowsVideoPreview.Services;

namespace WindowsVideoPreview.ViewModels;

public enum CompareLayoutMode
{
    SideBySide,
    Overlay
}

public class MainViewModel : ViewModelBase
{
    private readonly PlaybackCoordinator _coordinator;
    private readonly FfmpegService _ffmpegService;
    private readonly WaveformService _waveformService;
    private readonly SettingsService _settingsService;

    private MediaItem? _fileA;
    private MediaItem? _fileB;
    private MediaMetadata? _metadataA;
    private MediaMetadata? _metadataB;

    private bool _isCompareMode;
    private CompareLayoutMode _compareLayout = CompareLayoutMode.SideBySide;
    private double _overlayOpacity = 0.5;
    private bool _isWaveformVisible = true;
    private bool _isPlaylistVisible = true;
    private bool _isPlaying;
    private bool _isFullscreen;

    public event Action? ViewportLayoutChanged;
    public event Action<double>? OverlayOpacityChanged;
    public event Action? FileEndedA;
    public event Action? FileEndedB;

    private double _position;
    private double _duration;
    private double _offsetSeconds;
    private double _volume = 100.0;
    private bool _isMuted;
    private double _playbackSpeed = 1.0;
    private AudioSourceOption _selectedAudioSource = AudioSourceOption.AudioA;

    private double? _loopStart;
    private double? _loopEnd;

    private string _timecodeA = "00:00:00.000";
    private string _timecodeB = "00:00:00.000";
    private string _frameTextA = "Frame: 0 / 0";
    private string _frameTextB = "Frame: 0 / 0";
    private string _statusMessage = "Ready";

    private float[] _waveformData = Array.Empty<float>();
    private MediaItem? _selectedPlaylistItem;

    public PlaybackCoordinator Coordinator => _coordinator;
    public ObservableCollection<MediaItem> Playlist { get; } = new();

    #region Properties

    public MediaItem? FileA
    {
        get => _fileA;
        set => SetProperty(ref _fileA, value);
    }

    public MediaItem? FileB
    {
        get => _fileB;
        set => SetProperty(ref _fileB, value);
    }

    public MediaMetadata? MetadataA
    {
        get => _metadataA;
        set => SetProperty(ref _metadataA, value);
    }

    public MediaMetadata? MetadataB
    {
        get => _metadataB;
        set => SetProperty(ref _metadataB, value);
    }

    public bool IsCompareMode
    {
        get => _isCompareMode;
        set
        {
            if (SetProperty(ref _isCompareMode, value))
            {
                OnPropertyChanged(nameof(IsSingleMode));
                OnPropertyChanged(nameof(IsSideBySide));
                OnPropertyChanged(nameof(IsOverlay));
                _coordinator.Mode = value ? PlaybackMode.Compare : PlaybackMode.Single;
                _coordinator.ApplyAudioRouting();
                ViewportLayoutChanged?.Invoke();
            }
        }
    }

    public bool IsSingleMode => !IsCompareMode;

    public CompareLayoutMode CompareLayout
    {
        get => _compareLayout;
        set
        {
            if (SetProperty(ref _compareLayout, value))
            {
                OnPropertyChanged(nameof(IsSideBySide));
                OnPropertyChanged(nameof(IsOverlay));
                ViewportLayoutChanged?.Invoke();
            }
        }
    }

    public bool IsSideBySide => IsCompareMode && CompareLayout == CompareLayoutMode.SideBySide;
    public bool IsOverlay => IsCompareMode && CompareLayout == CompareLayoutMode.Overlay;

    public double OverlayOpacity
    {
        get => _overlayOpacity;
        set
        {
            if (SetProperty(ref _overlayOpacity, Math.Clamp(value, 0.0, 1.0)))
            {
                OverlayOpacityChanged?.Invoke(_overlayOpacity);
            }
        }
    }

    public bool IsWaveformVisible
    {
        get => _isWaveformVisible;
        set
        {
            if (SetProperty(ref _isWaveformVisible, value))
            {
                _settingsService.Current.ShowWaveform = value;
                _settingsService.Save();
            }
        }
    }

    public bool IsPlaylistVisible
    {
        get => _isPlaylistVisible;
        set
        {
            if (SetProperty(ref _isPlaylistVisible, value))
            {
                _settingsService.Current.ShowPlaylist = value;
                _settingsService.Save();
            }
        }
    }

    public bool IsPlaying
    {
        get => _isPlaying;
        set
        {
            if (SetProperty(ref _isPlaying, value))
            {
                OnPropertyChanged(nameof(PlayPauseIcon));
            }
        }
    }

    public string PlayPauseIcon => IsPlaying ? "❚❚" : "▶";

    public bool IsFullscreen
    {
        get => _isFullscreen;
        set => SetProperty(ref _isFullscreen, value);
    }

    public double Position
    {
        get => _position;
        set
        {
            if (Math.Abs(_position - value) > 0.001)
            {
                _position = value;
                OnPropertyChanged();
                UpdateTimecodesAndFrames();
            }
        }
    }

    public double Duration
    {
        get => _duration;
        set => SetProperty(ref _duration, value);
    }

    public double OffsetSeconds
    {
        get => _offsetSeconds;
        set
        {
            if (SetProperty(ref _offsetSeconds, Math.Round(value, 3)))
            {
                _coordinator.UpdateOffset(_offsetSeconds);
                UpdateTimecodesAndFrames();
            }
        }
    }

    public double Volume
    {
        get => _volume;
        set
        {
            if (SetProperty(ref _volume, value))
            {
                _coordinator.SetVolume(value);
                _settingsService.Current.DefaultVolume = value;
                _settingsService.Save();
            }
        }
    }

    public bool IsMuted
    {
        get => _isMuted;
        set
        {
            if (SetProperty(ref _isMuted, value))
            {
                _coordinator.SetMute(value);
                OnPropertyChanged(nameof(MuteIcon));
            }
        }
    }

    public string MuteIcon => IsMuted ? "🔇" : "🔊";

    public double PlaybackSpeed
    {
        get => _playbackSpeed;
        set
        {
            if (SetProperty(ref _playbackSpeed, value))
            {
                _coordinator.SetSpeed(value);
            }
        }
    }

    public AudioSourceOption SelectedAudioSource
    {
        get => _selectedAudioSource;
        set
        {
            if (SetProperty(ref _selectedAudioSource, value))
            {
                _coordinator.SetAudioSource(value);
                UpdateActiveWaveform();
            }
        }
    }

    public double? LoopStart
    {
        get => _loopStart;
        set
        {
            if (SetProperty(ref _loopStart, value))
            {
                _coordinator.LoopStart = value;
                OnPropertyChanged(nameof(LoopSummaryText));
                OnPropertyChanged(nameof(HasLoopAB));
            }
        }
    }

    public double? LoopEnd
    {
        get => _loopEnd;
        set
        {
            if (SetProperty(ref _loopEnd, value))
            {
                _coordinator.LoopEnd = value;
                OnPropertyChanged(nameof(LoopSummaryText));
                OnPropertyChanged(nameof(HasLoopAB));
            }
        }
    }

    public bool HasLoopAB => LoopStart.HasValue && LoopEnd.HasValue && LoopEnd.Value > LoopStart.Value;

    public string LoopSummaryText => (LoopStart.HasValue || LoopEnd.HasValue)
        ? $"Loop: {(LoopStart.HasValue ? FormatTimecode(LoopStart.Value) : "00:00:00.000")} → {(LoopEnd.HasValue ? FormatTimecode(LoopEnd.Value) : "End")}"
        : "Loop: Off";

    public PlaybackLoopMode LoopMode
    {
        get => _coordinator.LoopMode;
        set
        {
            if (_coordinator.LoopMode != value)
            {
                _coordinator.LoopMode = value;
                _settingsService.Current.LoopMode = value.ToString();
                _settingsService.Save();
                OnPropertyChanged();
                OnPropertyChanged(nameof(LoopModeDisplayText));
                OnPropertyChanged(nameof(LoopModeToolTip));
            }
        }
    }

    public string LoopModeDisplayText => LoopMode == PlaybackLoopMode.Loop ? "Loop" : "Once";

    public string LoopModeToolTip => LoopMode == PlaybackLoopMode.Loop
        ? "Loop Mode: Repeat Forever [Ctrl+L]"
        : "Loop Mode: Play Once [Ctrl+L]";

    private string _timeDisplayFormat = "Timecode";
    public string TimeDisplayFormat
    {
        get => _timeDisplayFormat;
        set
        {
            if (SetProperty(ref _timeDisplayFormat, value))
            {
                _settingsService.Current.TimeDisplayFormat = value;
                _settingsService.Save();
                OnPropertyChanged(nameof(IsTimecodeMode));
                OnPropertyChanged(nameof(IsFrameMode));
                UpdateTimecodesAndFrames();
            }
        }
    }

    public bool IsTimecodeMode
    {
        get => TimeDisplayFormat == "Timecode";
        set { if (value) TimeDisplayFormat = "Timecode"; }
    }

    public bool IsFrameMode
    {
        get => TimeDisplayFormat == "Frames";
        set { if (value) TimeDisplayFormat = "Frames"; }
    }

    private string _currentPlayheadText = "00:00:00.000";
    private string _totalDurationText = "00:00:00.000";
    private string _fpsSummaryText = "0.00 fps";

    public string CurrentPlayheadText
    {
        get => _currentPlayheadText;
        set => SetProperty(ref _currentPlayheadText, value);
    }

    public string TotalDurationText
    {
        get => _totalDurationText;
        set => SetProperty(ref _totalDurationText, value);
    }

    public string FpsSummaryText
    {
        get => _fpsSummaryText;
        set => SetProperty(ref _fpsSummaryText, value);
    }

    public HotkeyManager HotkeyManager { get; } = new();

    public string TimecodeA
    {
        get => _timecodeA;
        set => SetProperty(ref _timecodeA, value);
    }

    public string TimecodeB
    {
        get => _timecodeB;
        set => SetProperty(ref _timecodeB, value);
    }

    public string FrameTextA
    {
        get => _frameTextA;
        set => SetProperty(ref _frameTextA, value);
    }

    public string FrameTextB
    {
        get => _frameTextB;
        set => SetProperty(ref _frameTextB, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    public float[] WaveformData
    {
        get => _waveformData;
        set => SetProperty(ref _waveformData, value);
    }

    public MediaItem? SelectedPlaylistItem
    {
        get => _selectedPlaylistItem;
        set => SetProperty(ref _selectedPlaylistItem, value);
    }

    #endregion

    #region Commands

    public ICommand PlayPauseCommand { get; }
    public ICommand FrameStepForwardCommand { get; }
    public ICommand FrameStepBackwardCommand { get; }
    public ICommand SeekRelativeCommand { get; }
    public ICommand ToggleCompareModeCommand { get; }
    public ICommand ToggleWaveformCommand { get; }
    public ICommand TogglePlaylistCommand { get; }
    public ICommand ToggleFullscreenCommand { get; }
    public ICommand CycleLoopModeCommand { get; }
    public ICommand SetTimeFormatCommand { get; }
    public ICommand OpenHotkeySettingsCommand { get; }
    public ICommand SetLoopACommand { get; }
    public ICommand SetLoopBCommand { get; }
    public ICommand ClearLoopCommand { get; }
    public ICommand AdjustOffsetCommand { get; }
    public ICommand ResetOffsetCommand { get; }
    public ICommand OpenFileACommand { get; }
    public ICommand OpenFileBCommand { get; }
    public ICommand CloseFileBCommand { get; }
    public ICommand AddFilesCommand { get; }
    public ICommand RemovePlaylistItemCommand { get; }
    public ICommand ClearPlaylistCommand { get; }
    public ICommand PlayPlaylistItemCommand { get; }
    public ICommand SavePlaylistCommand { get; }
    public ICommand LoadPlaylistCommand { get; }
    public ICommand SaveCompareSessionCommand { get; }
    public ICommand LoadCompareSessionCommand { get; }
    public ICommand SetCompareLayoutCommand { get; }
    public ICommand SetOverlayOpacityCommand { get; }

    #endregion

    public MainViewModel(
        PlaybackCoordinator coordinator,
        FfmpegService ffmpegService,
        WaveformService waveformService,
        SettingsService settingsService)
    {
        _coordinator = coordinator;
        _ffmpegService = ffmpegService;
        _waveformService = waveformService;
        _settingsService = settingsService;

        // Restore settings
        var settings = _settingsService.Current;
        _isWaveformVisible = settings.ShowWaveform;
        _isPlaylistVisible = settings.ShowPlaylist;
        _volume = settings.DefaultVolume;
        _coordinator.MasterVolume = _volume;
        _timeDisplayFormat = string.IsNullOrEmpty(settings.TimeDisplayFormat) ? "Timecode" : settings.TimeDisplayFormat;
        if (Enum.TryParse<PlaybackLoopMode>(settings.LoopMode, out var lm))
        {
            _coordinator.LoopMode = lm;
        }
        HotkeyManager.LoadCustomHotkeys(settings.CustomHotkeys);

        // Wire coordinator events
        _coordinator.MasterPositionChanged += OnMasterPositionChanged;
        _coordinator.PlayStateChanged += state => Application.Current.Dispatcher.Invoke(() => IsPlaying = state);
        _coordinator.PlayerA.DurationChanged += OnDurationAChanged;
        _coordinator.PlayerA.FileLoaded += OnFileALoaded;
        _coordinator.PlayerB.FileLoaded += OnFileBLoaded;
        _coordinator.PlayerA.FileEnded += () => Application.Current.Dispatcher.Invoke(() => OnFileAEnded());
        _coordinator.PlayerB.FileEnded += () => Application.Current.Dispatcher.Invoke(() => OnFileBEnded());

        // Commands
        PlayPauseCommand = new RelayCommand(() => _coordinator.TogglePlayPause());
        FrameStepForwardCommand = new RelayCommand(() => _coordinator.StepFrameForward());
        FrameStepBackwardCommand = new RelayCommand(() => _coordinator.StepFrameBackward());
        SeekRelativeCommand = new RelayCommand(param =>
        {
            if (param is string s && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var delta))
            {
                _coordinator.SeekRelative(delta);
            }
        });

        ToggleCompareModeCommand = new RelayCommand(() => IsCompareMode = !IsCompareMode);
        ToggleWaveformCommand = new RelayCommand(() => IsWaveformVisible = !IsWaveformVisible);
        TogglePlaylistCommand = new RelayCommand(() => IsPlaylistVisible = !IsPlaylistVisible);
        ToggleFullscreenCommand = new RelayCommand(() => IsFullscreen = !IsFullscreen);
        CycleLoopModeCommand = new RelayCommand(() =>
        {
            LoopMode = LoopMode == PlaybackLoopMode.NoLoop ? PlaybackLoopMode.Loop : PlaybackLoopMode.NoLoop;
        });
        SetTimeFormatCommand = new RelayCommand(param =>
        {
            if (param is string fmt)
            {
                TimeDisplayFormat = fmt;
            }
        });
        OpenHotkeySettingsCommand = new RelayCommand(() => OpenHotkeySettingsDialog());

        SetLoopACommand = new RelayCommand(() => LoopStart = Position);
        SetLoopBCommand = new RelayCommand(() => LoopEnd = Position);
        ClearLoopCommand = new RelayCommand(() =>
        {
            LoopStart = null;
            LoopEnd = null;
        });

        AdjustOffsetCommand = new RelayCommand(param =>
        {
            if (param is string s && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var delta))
            {
                OffsetSeconds += delta;
            }
        });
        ResetOffsetCommand = new RelayCommand(() => OffsetSeconds = 0.0);

        OpenFileACommand = new RelayCommand(() => BrowseAndOpenFileA());
        OpenFileBCommand = new RelayCommand(() => BrowseAndOpenFileB());
        CloseFileBCommand = new RelayCommand(() =>
        {
            FileB = null;
            MetadataB = null;
            IsCompareMode = false;
        });

        AddFilesCommand = new RelayCommand(() => BrowseAndAddFilesToPlaylist());
        RemovePlaylistItemCommand = new RelayCommand(param =>
        {
            if (param is MediaItem item) Playlist.Remove(item);
            else if (SelectedPlaylistItem != null) Playlist.Remove(SelectedPlaylistItem);
        });
        ClearPlaylistCommand = new RelayCommand(() => Playlist.Clear());
        PlayPlaylistItemCommand = new RelayCommand(param =>
        {
            if (param is MediaItem item) LoadVideoA(item.FilePath);
        });

        SavePlaylistCommand = new RelayCommand(() => SavePlaylistDialog());
        LoadPlaylistCommand = new RelayCommand(() => LoadPlaylistDialog());
        SaveCompareSessionCommand = new RelayCommand(() => SaveCompareSessionDialog());
        LoadCompareSessionCommand = new RelayCommand(() => LoadCompareSessionDialog());

        SetCompareLayoutCommand = new RelayCommand(param =>
        {
            if (param is string s && Enum.TryParse<CompareLayoutMode>(s, true, out var m))
            {
                CompareLayout = m;
            }
            else if (param is CompareLayoutMode mode)
            {
                CompareLayout = mode;
            }
        });

        SetOverlayOpacityCommand = new RelayCommand(param =>
        {
            if (param is string s && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var op))
            {
                OverlayOpacity = op;
            }
            else if (param is double d)
            {
                OverlayOpacity = d;
            }
        });

        // Restore last playlist if available
        if (settings.LastPlaylistFiles != null && settings.LastPlaylistFiles.Count > 0)
        {
            foreach (var f in settings.LastPlaylistFiles)
            {
                if (File.Exists(f))
                {
                    Playlist.Add(new MediaItem(f));
                }
            }
        }
    }

    private void OnMasterPositionChanged(double pos)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            Position = pos;
        });
    }

    private void OnDurationAChanged(double dur)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            Duration = dur;
        });
    }

    private void OnFileAEnded()
    {
        StatusMessage = "Video A: Playback ended";
        FileEndedA?.Invoke();
    }

    private void OnFileBEnded()
    {
        StatusMessage = "Video B: Playback ended";
        FileEndedB?.Invoke();
    }

    private void OnFileALoaded()
    {
        Application.Current.Dispatcher.Invoke(async () =>
        {
            if (FileA != null)
            {
                StatusMessage = $"Loaded Video A: {FileA.FileName}";
                var meta = await _ffmpegService.GetMetadataAsync(FileA.FilePath);
                if (meta != null)
                {
                    MetadataA = meta;
                    FileA.Metadata = meta;
                    FileA.Duration = meta.Duration;
                    FileA.Resolution = meta.Resolution;
                    FileA.FpsText = meta.FpsText;
                    FileA.Codec = meta.VideoCodec;
                    if (meta.DurationSeconds > 0 && Duration <= 0)
                    {
                        Duration = meta.DurationSeconds;
                    }
                }
                UpdateTimecodesAndFrames();
                UpdateActiveWaveform();
            }
        });
    }

    private void OnFileBLoaded()
    {
        Application.Current.Dispatcher.Invoke(async () =>
        {
            if (FileB != null)
            {
                StatusMessage = $"Loaded Video B: {FileB.FileName}";
                var meta = await _ffmpegService.GetMetadataAsync(FileB.FilePath);
                if (meta != null)
                {
                    MetadataB = meta;
                    FileB.Metadata = meta;
                    FileB.Duration = meta.Duration;
                    FileB.Resolution = meta.Resolution;
                    FileB.FpsText = meta.FpsText;
                    FileB.Codec = meta.VideoCodec;
                }
                UpdateTimecodesAndFrames();
                if (SelectedAudioSource == AudioSourceOption.AudioB)
                {
                    UpdateActiveWaveform();
                }
            }
        });
    }

    public void UpdateActiveWaveform()
    {
        string? targetPath = null;
        if (SelectedAudioSource == AudioSourceOption.AudioB && FileB != null && File.Exists(FileB.FilePath))
        {
            targetPath = FileB.FilePath;
        }
        else if (SelectedAudioSource == AudioSourceOption.AudioA && FileA != null && File.Exists(FileA.FilePath))
        {
            targetPath = FileA.FilePath;
        }
        else if (FileA != null && File.Exists(FileA.FilePath))
        {
            targetPath = FileA.FilePath;
        }

        if (string.IsNullOrEmpty(targetPath))
        {
            WaveformData = Array.Empty<float>();
            return;
        }

        WaveformData = Array.Empty<float>();
        _ = _waveformService.GetWaveformAsync(targetPath, wf =>
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                WaveformData = wf;
            });
        });
    }

    public void LoadVideoA(string filePath)
    {
        if (!File.Exists(filePath)) return;
        FileA = new MediaItem(filePath);
        _coordinator.PlayerA.Load(filePath);
        StatusMessage = $"Opening {FileA.FileName}...";

        // Also add to playlist if not present
        if (!Playlist.Any(p => p.FilePath.Equals(filePath, StringComparison.OrdinalIgnoreCase)))
        {
            Playlist.Add(FileA);
            SaveCurrentPlaylistToSettings();
        }
    }

    public void LoadVideoB(string filePath)
    {
        if (!File.Exists(filePath)) return;
        FileB = new MediaItem(filePath);
        _coordinator.PlayerB.Load(filePath);
        IsCompareMode = true;
        StatusMessage = $"Compare mode active: Video B is {FileB.FileName}";
    }

    public void UserSeek(double targetSeconds)
    {
        _coordinator.Seek(targetSeconds, exact: true);
        Position = targetSeconds;
    }

    public void JumpToTimecodeOrFrame(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return;
        input = input.Trim();

        // Check if frame format '#1234' or number
        if (input.StartsWith("#") && long.TryParse(input.Substring(1), out var frameNum))
        {
            double fps = MetadataA?.Fps ?? _coordinator.PlayerA.Fps;
            if (fps > 0)
            {
                UserSeek(frameNum / fps);
                return;
            }
        }
        else if (long.TryParse(input, out var directFrame) && directFrame > 500 && (MetadataA?.Fps ?? 0) > 0)
        {
            // If it's a large integer, interpret as frame number
            UserSeek(directFrame / MetadataA!.Fps);
            return;
        }

        // Try parse as TimeSpan: hh:mm:ss.fff or mm:ss.fff or seconds
        if (TimeSpan.TryParse(input, CultureInfo.InvariantCulture, out var ts))
        {
            UserSeek(ts.TotalSeconds);
            return;
        }

        if (double.TryParse(input, NumberStyles.Float, CultureInfo.InvariantCulture, out var sec))
        {
            UserSeek(sec);
        }
    }

    private void UpdateTimecodesAndFrames()
    {
        double posA = Position;
        TimecodeA = FormatTimecode(posA);

        double fpsA = MetadataA?.Fps ?? _coordinator.PlayerA.Fps;
        long totalA = MetadataA?.TotalFrames ?? _coordinator.PlayerA.TotalFrames;
        long currentA = fpsA > 0 ? (long)Math.Floor(posA * fpsA) : 0;
        FrameTextA = totalA > 0 ? $"Frame: {currentA} / {totalA}" : $"Frame: {currentA}";

        double posB = Math.Max(0, posA - OffsetSeconds);
        TimecodeB = FormatTimecode(posB);

        double fpsB = MetadataB?.Fps ?? _coordinator.PlayerB.Fps;
        long totalB = MetadataB?.TotalFrames ?? _coordinator.PlayerB.TotalFrames;
        long currentB = fpsB > 0 ? (long)Math.Floor(posB * fpsB) : 0;
        FrameTextB = totalB > 0 ? $"Frame: {currentB} / {totalB}" : $"Frame: {currentB}";

        // Timeline Floating Playhead & Metric Tracking
        if (IsFrameMode)
        {
            CurrentPlayheadText = $"{Math.Max(0, currentA)}";
            TotalDurationText = totalA > 0 ? $"{totalA} frames" : "0 frames";
        }
        else
        {
            CurrentPlayheadText = FormatTimecode(posA);
            TotalDurationText = FormatTimecode(Duration);
        }

        FpsSummaryText = fpsA > 0 ? $"{fpsA:F2} fps" : "0.00 fps";
        OnPropertyChanged(nameof(HasLoopAB));
    }

    private void OpenHotkeySettingsDialog()
    {
        var win = Application.Current.MainWindow;
        var dlg = new Views.HotkeySettingsDialog(HotkeyManager)
        {
            Owner = win
        };
        if (dlg.ShowDialog() == true)
        {
            _settingsService.Current.CustomHotkeys = HotkeyManager.ExportCustomHotkeys();
            _settingsService.Save();
            OnPropertyChanged(nameof(LoopModeToolTip));
            StatusMessage = "Keyboard shortcuts updated successfully.";
        }
    }

    public static string FormatTimecode(double seconds)
    {
        if (seconds < 0) seconds = 0;
        var ts = TimeSpan.FromSeconds(seconds);
        return $"{(int)ts.TotalHours:D2}:{ts.Minutes:D2}:{ts.Seconds:D2}.{ts.Milliseconds:D3}";
    }

    private void BrowseAndOpenFileA()
    {
        var dlg = new OpenFileDialog
        {
            Title = "Open Video A",
            Filter = "Video Files|*.mp4;*.mov;*.mkv;*.avi;*.webm;*.flv;*.wmv;*.ts;*.m4v|All Files|*.*"
        };
        if (dlg.ShowDialog() == true)
        {
            LoadVideoA(dlg.FileName);
        }
    }

    private void BrowseAndOpenFileB()
    {
        var dlg = new OpenFileDialog
        {
            Title = "Open Video B (Compare Mode)",
            Filter = "Video Files|*.mp4;*.mov;*.mkv;*.avi;*.webm;*.flv;*.wmv;*.ts;*.m4v|All Files|*.*"
        };
        if (dlg.ShowDialog() == true)
        {
            LoadVideoB(dlg.FileName);
        }
    }

    private void BrowseAndAddFilesToPlaylist()
    {
        var dlg = new OpenFileDialog
        {
            Title = "Add Videos to Playlist",
            Filter = "Video Files|*.mp4;*.mov;*.mkv;*.avi;*.webm;*.flv;*.wmv;*.ts;*.m4v|All Files|*.*",
            Multiselect = true
        };
        if (dlg.ShowDialog() == true)
        {
            foreach (var file in dlg.FileNames)
            {
                if (!Playlist.Any(p => p.FilePath.Equals(file, StringComparison.OrdinalIgnoreCase)))
                {
                    Playlist.Add(new MediaItem(file));
                }
            }
            SaveCurrentPlaylistToSettings();
        }
    }

    private void SaveCurrentPlaylistToSettings()
    {
        _settingsService.Current.LastPlaylistFiles = Playlist.Select(p => p.FilePath).ToList();
        _settingsService.Save();
    }

    private void SavePlaylistDialog()
    {
        var dlg = new SaveFileDialog
        {
            Title = "Save Playlist",
            Filter = "Playlist JSON (*.json)|*.json|M3U Playlist (*.m3u)|*.m3u",
            DefaultExt = ".json"
        };
        if (dlg.ShowDialog() == true)
        {
            try
            {
                if (dlg.FileName.EndsWith(".m3u", StringComparison.OrdinalIgnoreCase))
                {
                    var lines = Playlist.Select(p => p.FilePath);
                    File.WriteAllLines(dlg.FileName, lines);
                }
                else
                {
                    var files = Playlist.Select(p => p.FilePath).ToList();
                    var json = JsonSerializer.Serialize(files, new JsonSerializerOptions { WriteIndented = true });
                    File.WriteAllText(dlg.FileName, json);
                }
                StatusMessage = $"Playlist saved to {Path.GetFileName(dlg.FileName)}";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error saving playlist: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private void LoadPlaylistDialog()
    {
        var dlg = new OpenFileDialog
        {
            Title = "Open Playlist",
            Filter = "Playlist Files (*.json;*.m3u)|*.json;*.m3u|All Files|*.*"
        };
        if (dlg.ShowDialog() == true)
        {
            try
            {
                Playlist.Clear();
                if (dlg.FileName.EndsWith(".m3u", StringComparison.OrdinalIgnoreCase))
                {
                    var lines = File.ReadAllLines(dlg.FileName);
                    foreach (var line in lines)
                    {
                        var trimmed = line.Trim();
                        if (!string.IsNullOrEmpty(trimmed) && !trimmed.StartsWith("#") && File.Exists(trimmed))
                        {
                            Playlist.Add(new MediaItem(trimmed));
                        }
                    }
                }
                else
                {
                    var json = File.ReadAllText(dlg.FileName);
                    var files = JsonSerializer.Deserialize<string[]>(json);
                    if (files != null)
                    {
                        foreach (var f in files)
                        {
                            if (File.Exists(f))
                            {
                                Playlist.Add(new MediaItem(f));
                            }
                        }
                    }
                }
                SaveCurrentPlaylistToSettings();
                StatusMessage = $"Loaded playlist ({Playlist.Count} items)";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading playlist: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private void SaveCompareSessionDialog()
    {
        if (FileA == null || FileB == null)
        {
            MessageBox.Show("Please load both Video A and Video B before saving a Compare session.", "Notice", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dlg = new SaveFileDialog
        {
            Title = "Save Compare Session",
            Filter = "Compare Session (*.json)|*.json",
            FileName = "compare_session.json"
        };
        if (dlg.ShowDialog() == true)
        {
            try
            {
                var session = new CompareSession
                {
                    VideoAPath = FileA.FilePath,
                    VideoBPath = FileB.FilePath,
                    OffsetSeconds = OffsetSeconds,
                    LastPositionSeconds = Position,
                    AudioSource = SelectedAudioSource.ToString(),
                    LoopStart = LoopStart,
                    LoopEnd = LoopEnd
                };
                var json = JsonSerializer.Serialize(session, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(dlg.FileName, json);
                StatusMessage = $"Compare session saved to {Path.GetFileName(dlg.FileName)}";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error saving session: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private void LoadCompareSessionDialog()
    {
        var dlg = new OpenFileDialog
        {
            Title = "Load Compare Session",
            Filter = "Compare Session (*.json)|*.json|All Files|*.*"
        };
        if (dlg.ShowDialog() == true)
        {
            try
            {
                var json = File.ReadAllText(dlg.FileName);
                var session = JsonSerializer.Deserialize<CompareSession>(json);
                if (session != null && File.Exists(session.VideoAPath) && File.Exists(session.VideoBPath))
                {
                    LoadVideoA(session.VideoAPath);
                    LoadVideoB(session.VideoBPath);
                    OffsetSeconds = session.OffsetSeconds;
                    LoopStart = session.LoopStart;
                    LoopEnd = session.LoopEnd;
                    if (Enum.TryParse<AudioSourceOption>(session.AudioSource, out var src))
                    {
                        SelectedAudioSource = src;
                    }
                    if (session.LastPositionSeconds > 0)
                    {
                        Task.Delay(500).ContinueWith(_ =>
                        {
                            Application.Current.Dispatcher.Invoke(() => UserSeek(session.LastPositionSeconds));
                        });
                    }
                    StatusMessage = "Compare session restored successfully!";
                }
                else
                {
                    MessageBox.Show("One or more video files in this session could not be found.", "File Not Found", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading session: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}

using System;
using System.Collections.Generic;

namespace WindowsVideoPreview.Models;

public class AppSettings
{
    public bool ShowWaveform { get; set; } = true;
    public bool ShowPlaylist { get; set; } = true;
    public double DefaultVolume { get; set; } = 100.0;
    public double PlaybackSpeed { get; set; } = 1.0;
    public string TimeDisplayFormat { get; set; } = "Timecode"; // "Timecode" or "Frames"
    public string LoopMode { get; set; } = "NoLoop"; // "NoLoop", "Loop", "PingPong"
    public List<string> RecentFiles { get; set; } = new();
    public List<string> LastPlaylistFiles { get; set; } = new();
    public CompareSession? LastCompareSession { get; set; }
    public Dictionary<string, string> CustomHotkeys { get; set; } = new();
}

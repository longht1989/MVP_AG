using System;

namespace WindowsVideoPreview.Models;

public class CompareSession
{
    public string VideoAPath { get; set; } = string.Empty;
    public string VideoBPath { get; set; } = string.Empty;
    public double OffsetSeconds { get; set; }
    public double LastPositionSeconds { get; set; }
    public string AudioSource { get; set; } = "A"; // "A", "B", "Mute"
    public double? LoopStart { get; set; }
    public double? LoopEnd { get; set; }
    public DateTime SavedAt { get; set; } = DateTime.Now;
}

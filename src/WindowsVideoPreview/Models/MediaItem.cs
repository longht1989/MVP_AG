using System;
using System.IO;

namespace WindowsVideoPreview.Models;

public class MediaItem
{
    public string FilePath { get; set; } = string.Empty;
    public string FileName => Path.GetFileName(FilePath);
    public string Directory => Path.GetDirectoryName(FilePath) ?? string.Empty;
    public TimeSpan Duration { get; set; } = TimeSpan.Zero;
    public string DurationText => Duration.TotalHours >= 1 
        ? Duration.ToString(@"hh\:mm\:ss") 
        : Duration.ToString(@"mm\:ss");
    public string Resolution { get; set; } = "---";
    public string FpsText { get; set; } = "---";
    public string Codec { get; set; } = "---";
    public long FileSizeBytes { get; set; }
    public MediaMetadata? Metadata { get; set; }

    public MediaItem() { }

    public MediaItem(string filePath)
    {
        FilePath = filePath;
        if (File.Exists(filePath))
        {
            var info = new FileInfo(filePath);
            FileSizeBytes = info.Length;
        }
    }
}

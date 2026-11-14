using System;
using System.Text.Json.Serialization;

namespace WindowsVideoPreview.Models;

public class MediaMetadata
{
    public string FilePath { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public TimeSpan Duration { get; set; } = TimeSpan.Zero;
    public double DurationSeconds => Duration.TotalSeconds;
    
    // Video Info
    public int Width { get; set; }
    public int Height { get; set; }
    public string Resolution => (Width > 0 && Height > 0) ? $"{Width}x{Height}" : "N/A";
    public string VideoCodec { get; set; } = "N/A";
    public double Fps { get; set; }
    public string FpsText => Fps > 0 ? $"{Fps:F2} fps" : "N/A";
    public long TotalFrames { get; set; }
    public string AspectRatio { get; set; } = "16:9";
    public string PixelFormat { get; set; } = "N/A";
    public long VideoBitrate { get; set; }

    // Audio Info
    public bool HasAudio { get; set; }
    public string AudioCodec { get; set; } = "N/A";
    public int SampleRate { get; set; }
    public int Channels { get; set; }
    public string AudioInfo => HasAudio 
        ? $"{AudioCodec.ToUpper()} | {SampleRate} Hz | {(Channels == 2 ? "Stereo" : Channels == 1 ? "Mono" : $"{Channels}ch")}"
        : "No Audio";

    public long FileSizeBytes { get; set; }
    public string FileSizeText
    {
        get
        {
            if (FileSizeBytes < 1024) return $"{FileSizeBytes} B";
            if (FileSizeBytes < 1024 * 1024) return $"{FileSizeBytes / 1024.0:F1} KB";
            if (FileSizeBytes < 1024 * 1024 * 1024) return $"{FileSizeBytes / (1024.0 * 1024.0):F1} MB";
            return $"{FileSizeBytes / (1024.0 * 1024.0 * 1024.0):F2} GB";
        }
    }

    public string RawDetails { get; set; } = string.Empty;
}

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WindowsVideoPreview.Models;

namespace WindowsVideoPreview.Services;

public class FfmpegService
{
    private readonly string _ffprobePath;
    private readonly string _ffmpegPath;

    public FfmpegService()
    {
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        var localFfprobe = Path.Combine(baseDir, "ffprobe.exe");
        var localFfmpeg = Path.Combine(baseDir, "ffmpeg.exe");

        _ffprobePath = File.Exists(localFfprobe) ? localFfprobe : "ffprobe";
        _ffmpegPath = File.Exists(localFfmpeg) ? localFfmpeg : "ffmpeg";
    }

    public async Task<MediaMetadata?> GetMetadataAsync(string filePath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(filePath)) return null;

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = _ffprobePath,
                Arguments = $"-v error -show_format -show_streams -of json \"{filePath}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = new Process { StartInfo = startInfo };
            process.Start();

            var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            var json = await outputTask;

            if (string.IsNullOrWhiteSpace(json)) return null;

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var metadata = new MediaMetadata
            {
                FilePath = filePath,
                FileName = Path.GetFileName(filePath),
                RawDetails = json
            };

            if (File.Exists(filePath))
            {
                metadata.FileSizeBytes = new FileInfo(filePath).Length;
            }

            // Parse format
            if (root.TryGetProperty("format", out var formatElem))
            {
                if (formatElem.TryGetProperty("duration", out var durElem) && 
                    double.TryParse(durElem.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var durSec))
                {
                    metadata.Duration = TimeSpan.FromSeconds(durSec);
                }
                if (formatElem.TryGetProperty("bit_rate", out var brElem) && 
                    long.TryParse(brElem.GetString(), out var br))
                {
                    metadata.VideoBitrate = br;
                }
            }

            // Parse streams
            if (root.TryGetProperty("streams", out var streamsElem) && streamsElem.ValueKind == JsonValueKind.Array)
            {
                foreach (var stream in streamsElem.EnumerateArray())
                {
                    var codecType = stream.TryGetProperty("codec_type", out var ct) ? ct.GetString() : null;

                    if (codecType == "video" && metadata.Width == 0)
                    {
                        if (stream.TryGetProperty("width", out var w)) metadata.Width = w.GetInt32();
                        if (stream.TryGetProperty("height", out var h)) metadata.Height = h.GetInt32();
                        if (stream.TryGetProperty("codec_name", out var cn)) metadata.VideoCodec = cn.GetString() ?? "Unknown";
                        if (stream.TryGetProperty("pix_fmt", out var pf)) metadata.PixelFormat = pf.GetString() ?? "Unknown";
                        if (stream.TryGetProperty("display_aspect_ratio", out var dar)) metadata.AspectRatio = dar.GetString() ?? "Unknown";

                        // FPS calculation
                        if (stream.TryGetProperty("r_frame_rate", out var rfr))
                        {
                            var rfrStr = rfr.GetString();
                            metadata.Fps = ParseFrameRate(rfrStr);
                        }
                        if (metadata.Fps <= 0 && stream.TryGetProperty("avg_frame_rate", out var afr))
                        {
                            var afrStr = afr.GetString();
                            metadata.Fps = ParseFrameRate(afrStr);
                        }

                        // Total frames
                        if (stream.TryGetProperty("nb_frames", out var nbf) && long.TryParse(nbf.GetString(), out var frames))
                        {
                            metadata.TotalFrames = frames;
                        }
                        else if (metadata.DurationSeconds > 0 && metadata.Fps > 0)
                        {
                            metadata.TotalFrames = (long)Math.Round(metadata.DurationSeconds * metadata.Fps);
                        }
                    }
                    else if (codecType == "audio" && !metadata.HasAudio)
                    {
                        metadata.HasAudio = true;
                        if (stream.TryGetProperty("codec_name", out var acn)) metadata.AudioCodec = acn.GetString() ?? "Unknown";
                        if (stream.TryGetProperty("sample_rate", out var sr) && int.TryParse(sr.GetString(), out var srate)) metadata.SampleRate = srate;
                        if (stream.TryGetProperty("channels", out var ch)) metadata.Channels = ch.GetInt32();
                    }
                }
            }

            return metadata;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error querying ffprobe for {filePath}: {ex.Message}");
            return null;
        }
    }

    private static double ParseFrameRate(string? rfrStr)
    {
        if (string.IsNullOrEmpty(rfrStr)) return 0;
        var parts = rfrStr.Split('/');
        if (parts.Length == 2 &&
            double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var num) &&
            double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var den) &&
            den > 0)
        {
            return num / den;
        }
        if (double.TryParse(rfrStr, NumberStyles.Float, CultureInfo.InvariantCulture, out var val))
        {
            return val;
        }
        return 0;
    }

    public async Task<float[]> ExtractAudioPeaksAsync(string filePath, int targetSampleRate = 100, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(filePath)) return Array.Empty<float>();

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = _ffmpegPath,
                Arguments = $"-y -v error -nostats -i \"{filePath}\" -map 0:a:0? -vn -ac 1 -ar {targetSampleRate} -f s16le pipe:1",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = new Process { StartInfo = startInfo };
            process.Start();

            // Read stderr asynchronously to prevent pipe buffer deadlock on large video files
            var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);

            var peaks = new List<float>(4096);
            using var stream = process.StandardOutput.BaseStream;
            var buffer = new byte[4096];
            int bytesRead;

            while ((bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
            {
                for (int i = 0; i < bytesRead - 1; i += 2)
                {
                    short sample = (short)(buffer[i] | (buffer[i + 1] << 8));
                    float normalized = Math.Abs(sample) / 32768.0f;
                    peaks.Add(normalized);
                }
            }

            await stderrTask;
            await process.WaitForExitAsync(cancellationToken);

            if (peaks.Count == 0) return Array.Empty<float>();

            // Normalize audio dynamic range: apply gain so highest peaks are clearly visible
            float maxPeak = 0f;
            for (int i = 0; i < peaks.Count; i++)
            {
                if (peaks[i] > maxPeak) maxPeak = peaks[i];
            }

            if (maxPeak > 0.001f)
            {
                // Dynamic expansion with square-root curve for natural perceptual volume
                float scale = 1.0f / maxPeak;
                for (int i = 0; i < peaks.Count; i++)
                {
                    float normalized = Math.Clamp(peaks[i] * scale, 0f, 1f);
                    // Boost quieter parts slightly with sqrt so waveform details stand out
                    peaks[i] = (float)Math.Sqrt(normalized);
                }
            }

            return peaks.ToArray();
        }
        catch (OperationCanceledException)
        {
            return Array.Empty<float>();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Waveform extraction error: {ex.Message}");
            return Array.Empty<float>();
        }
    }
}

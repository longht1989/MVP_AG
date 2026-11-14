using System;
using System.Collections.Concurrent;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace WindowsVideoPreview.Services;

public class WaveformService
{
    private readonly FfmpegService _ffmpegService;
    private readonly string _cacheDirectory;
    private readonly ConcurrentDictionary<string, float[]> _memoryCache = new();
    private CancellationTokenSource? _currentCts;

    public WaveformService(FfmpegService ffmpegService)
    {
        _ffmpegService = ffmpegService;
        _cacheDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WindowsVideoPreview",
            "WaveformCache"
        );
        Directory.CreateDirectory(_cacheDirectory);
    }

    public async Task<float[]> GetWaveformAsync(string filePath, Action<float[]>? onReady = null)
    {
        _currentCts?.Cancel();
        _currentCts = new CancellationTokenSource();
        var token = _currentCts.Token;

        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
        {
            return Array.Empty<float>();
        }

        if (_memoryCache.TryGetValue(filePath, out var cached))
        {
            onReady?.Invoke(cached);
            return cached;
        }

        var diskCacheFile = GetCachePath(filePath);
        if (File.Exists(diskCacheFile))
        {
            try
            {
                var bytes = await File.ReadAllBytesAsync(diskCacheFile, token);
                var floats = new float[bytes.Length / sizeof(float)];
                Buffer.BlockCopy(bytes, 0, floats, 0, bytes.Length);
                _memoryCache[filePath] = floats;
                onReady?.Invoke(floats);
                return floats;
            }
            catch
            {
                // Cache read failed, proceed to extract
            }
        }

        // Run background extraction
        return await Task.Run(async () =>
        {
            try
            {
                var peaks = await _ffmpegService.ExtractAudioPeaksAsync(filePath, 100, token);
                if (peaks.Length > 0 && !token.IsCancellationRequested)
                {
                    _memoryCache[filePath] = peaks;

                    try
                    {
                        var bytes = new byte[peaks.Length * sizeof(float)];
                        Buffer.BlockCopy(peaks, 0, bytes, 0, bytes.Length);
                        await File.WriteAllBytesAsync(diskCacheFile, bytes, CancellationToken.None);
                    }
                    catch
                    {
                        // Cache write failure ignored
                    }

                    onReady?.Invoke(peaks);
                }
                return peaks;
            }
            catch (OperationCanceledException)
            {
                return Array.Empty<float>();
            }
        }, token);
    }

    private string GetCachePath(string filePath)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(filePath.ToLowerInvariant())));
        return Path.Combine(_cacheDirectory, $"{hash}.wf");
    }

    public void CancelCurrent()
    {
        _currentCts?.Cancel();
    }
}

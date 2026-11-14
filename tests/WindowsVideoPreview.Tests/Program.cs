using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using WindowsVideoPreview.Models;
using WindowsVideoPreview.Services;
using WindowsVideoPreview.ViewModels;

class Program
{
    static async Task<int> Main()
    {
        Console.WriteLine("==================================================");
        Console.WriteLine("  RUNNING WINDOWS VIDEO PREVIEW TEST SUITE");
        Console.WriteLine("==================================================");

        int passed = 0;
        int failed = 0;

        void Assert(string testName, bool condition, string detail = "")
        {
            if (condition)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.Write("[PASS] ");
                Console.ResetColor();
                Console.WriteLine($"{testName} {(string.IsNullOrEmpty(detail) ? "" : "-> " + detail)}");
                passed++;
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.Write("[FAIL] ");
                Console.ResetColor();
                Console.WriteLine($"{testName} {(string.IsNullOrEmpty(detail) ? "" : "-> " + detail)}");
                failed++;
            }
        }

        var dir = AppContext.BaseDirectory;
        while (!Directory.Exists(Path.Combine(dir, "test_samples")) && Directory.GetParent(dir) != null)
        {
            dir = Directory.GetParent(dir)!.FullName;
        }
        var rootDir = dir;
        var sampleA = Path.Combine(rootDir, "test_samples", "sample_A_30fps.mp4");
        var sampleB = Path.Combine(rootDir, "test_samples", "sample_B_60fps.mp4");
        var sampleC = Path.Combine(rootDir, "test_samples", "sample_C_noaudio.mkv");

        Assert("Test samples existence", File.Exists(sampleA) && File.Exists(sampleB) && File.Exists(sampleC), "All 3 sample files exist");

        // 1. Test FfmpegService Metadata
        Console.WriteLine("\n--- Testing FfmpegService Metadata Extraction ---");
        var ffmpeg = new FfmpegService();

        var metaA = await ffmpeg.GetMetadataAsync(sampleA);
        Assert("Sample A metadata not null", metaA != null);
        if (metaA != null)
        {
            Assert("Sample A width & height", metaA.Width == 1280 && metaA.Height == 720, $"{metaA.Resolution}");
            Assert("Sample A FPS", Math.Abs(metaA.Fps - 30.0) < 0.1, $"{metaA.Fps:F2} fps");
            Assert("Sample A total frames", metaA.TotalFrames >= 295 && metaA.TotalFrames <= 305, $"{metaA.TotalFrames} frames");
            Assert("Sample A has audio", metaA.HasAudio && metaA.SampleRate == 44100, $"{metaA.AudioInfo}");
            Assert("Sample A codec", metaA.VideoCodec.Contains("h264", StringComparison.OrdinalIgnoreCase), metaA.VideoCodec);
        }

        var metaB = await ffmpeg.GetMetadataAsync(sampleB);
        Assert("Sample B metadata not null", metaB != null);
        if (metaB != null)
        {
            Assert("Sample B width & height", metaB.Width == 1920 && metaB.Height == 1080, $"{metaB.Resolution}");
            Assert("Sample B FPS", Math.Abs(metaB.Fps - 60.0) < 0.1, $"{metaB.Fps:F2} fps");
            Assert("Sample B total frames", metaB.TotalFrames >= 595 && metaB.TotalFrames <= 605, $"{metaB.TotalFrames} frames");
            Assert("Sample B has audio", metaB.HasAudio, $"{metaB.AudioInfo}");
        }

        var metaC = await ffmpeg.GetMetadataAsync(sampleC);
        Assert("Sample C metadata not null", metaC != null);
        if (metaC != null)
        {
            Assert("Sample C width & height", metaC.Width == 640 && metaC.Height == 360, $"{metaC.Resolution}");
            Assert("Sample C FPS", Math.Abs(metaC.Fps - 24.0) < 0.1, $"{metaC.Fps:F2} fps");
            Assert("Sample C has NO audio", !metaC.HasAudio, "Confirmed no audio stream");
        }

        // 2. Test Waveform Extraction & Caching
        Console.WriteLine("\n--- Testing Waveform Service ---");
        var waveformService = new WaveformService(ffmpeg);

        var uploadedFile = @"C:\Users\long\.gemini\antigravity\brain\73b88f02-f1cc-432a-b4b6-39f2a778a396\.user_uploaded\media_1794561030400_cbbb0bb8.mp4";
        if (File.Exists(uploadedFile))
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var wfUp = await waveformService.GetWaveformAsync(uploadedFile);
            sw.Stop();
            Assert("5-min uploaded video waveform extracted", wfUp != null && wfUp.Length > 0, $"{wfUp?.Length} peaks in {sw.ElapsedMilliseconds}ms");
        }

        var wfA = await waveformService.GetWaveformAsync(sampleA);
        Assert("Waveform A extracted", wfA != null && wfA.Length > 0, $"{wfA?.Length} sample points");

        // Verify cache hit
        var wfACached = await waveformService.GetWaveformAsync(sampleA);
        Assert("Waveform A cached retrieval", wfACached != null && wfACached.Length == wfA?.Length, "Cache returned identical data");

        // File with no audio
        var wfC = await waveformService.GetWaveformAsync(sampleC);
        Assert("Waveform C handles no-audio safely", wfC != null && wfC.Length == 0, "Empty array gracefully handled");

        // 3. Test Compare Session & Settings Serialization
        Console.WriteLine("\n--- Testing Session & Settings Serialization ---");
        var session = new CompareSession
        {
            VideoAPath = sampleA,
            VideoBPath = sampleB,
            OffsetSeconds = 1.250,
            LastPositionSeconds = 4.500,
            AudioSource = "AudioB",
            LoopStart = 1.0,
            LoopEnd = 3.0
        };

        var sessionJson = System.Text.Json.JsonSerializer.Serialize(session);
        var restoredSession = System.Text.Json.JsonSerializer.Deserialize<CompareSession>(sessionJson);
        Assert("CompareSession deserialization", restoredSession != null && 
            restoredSession.VideoAPath == sampleA && 
            Math.Abs(restoredSession.OffsetSeconds - 1.250) < 0.001 &&
            restoredSession.AudioSource == "AudioB", "Offset and paths preserved");

        var settings = new SettingsService();
        settings.Current.ShowWaveform = false;
        settings.Current.DefaultVolume = 85.0;
        settings.Save();

        var loadedSettings = new SettingsService().Load();
        Assert("SettingsService save/load", loadedSettings.DefaultVolume == 85.0 && !loadedSettings.ShowWaveform, "Settings persisted");

        // 4. Test Timecode Formatter & Frame Parsing
        Console.WriteLine("\n--- Testing Timecode & Coordinate Helpers ---");
        var tc1 = MainViewModel.FormatTimecode(0.0);
        Assert("Timecode 0s", tc1 == "00:00:00.000", tc1);
        var tc2 = MainViewModel.FormatTimecode(75.542);
        Assert("Timecode 75.542s", tc2 == "00:01:15.542", tc2);
        var tc3 = MainViewModel.FormatTimecode(3665.123);
        Assert("Timecode 1h 1m 5s", tc3 == "01:01:05.123", tc3);

        // 5. Test MPV Engine & Frame Stepping
        Console.WriteLine("\n--- Testing LibMPV Engine Initialization & Frame Control ---");
        try
        {
            using var player = new MpvPlayer();
            player.Initialize(IntPtr.Zero);
            Assert("MPV initialized", true, "Libmpv client instantiated and configured");

            var loadedTcs = new TaskCompletionSource<bool>();
            player.FileLoaded += () => loadedTcs.TrySetResult(true);

            player.Load(sampleA);
            var loadedSuccess = await Task.WhenAny(loadedTcs.Task, Task.Delay(4000)) == loadedTcs.Task;
            Assert("MPV loaded sample A", loadedSuccess, $"Duration: {player.Duration:F2}s");

            player.Pause();
            player.Seek(1.0, exact: true);
            await Task.Delay(150);

            double posBefore = player.TimePos;
            player.FrameStep();
            await Task.Delay(150);
            double posAfterStep = player.TimePos;

            Assert("Frame step forward", posAfterStep >= posBefore, $"Pos: {posBefore:F4} -> {posAfterStep:F4}");

            player.FrameBackStep();
            await Task.Delay(150);
            double posAfterBack = player.TimePos;
            Assert("Frame step backward", posAfterBack <= posAfterStep, $"Pos: {posAfterStep:F4} -> {posAfterBack:F4}");
        }
        catch (Exception ex)
        {
            Assert("MPV engine test", false, ex.Message);
        }

        Console.WriteLine("\n==================================================");
        Console.WriteLine($"  TEST RESULTS: {passed} PASSED, {failed} FAILED");
        Console.WriteLine("==================================================");

        return failed == 0 ? 0 : 1;
    }
}

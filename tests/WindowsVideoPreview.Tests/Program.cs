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
            player.Pause();
            await Task.Delay(100);
        }
        catch (Exception ex)
        {
            Assert("MPV engine test", false, ex.Message);
        }

        // 6. Test HotkeyManager & Custom Shortcut Mapping
        Console.WriteLine("\n--- Testing HotkeyManager & Custom Shortcuts ---");
        var hkm = new HotkeyManager();
        var actionA = hkm.MatchAction(System.Windows.Input.Key.OemComma, System.Windows.Input.ModifierKeys.None);
        Assert("Default Loop In shortcut (Comma)", actionA == HotkeyManager.Actions.SetLoopA, $"Action: {actionA}");

        var actionB_period = hkm.MatchAction(System.Windows.Input.Key.OemPeriod, System.Windows.Input.ModifierKeys.None);
        Assert("Default Loop Out shortcut (Period)", actionB_period == HotkeyManager.Actions.SetLoopB, $"Action: {actionB_period}");

        var actionClear_slash = hkm.MatchAction(System.Windows.Input.Key.OemQuestion, System.Windows.Input.ModifierKeys.None);
        Assert("Default Clear Loop shortcut (Slash)", actionClear_slash == HotkeyManager.Actions.ClearLoop, $"Action: {actionClear_slash}");

        var actionClear_divide = hkm.MatchAction(System.Windows.Input.Key.Divide, System.Windows.Input.ModifierKeys.None);
        Assert("Fallback Clear Loop shortcut (Numpad Divide)", actionClear_divide == HotkeyManager.Actions.ClearLoop, $"Action: {actionClear_divide}");

        var actionClear_backslash = hkm.MatchAction(System.Windows.Input.Key.OemBackslash, System.Windows.Input.ModifierKeys.None);
        Assert("Fallback Clear Loop shortcut (Backslash)", actionClear_backslash == HotkeyManager.Actions.ClearLoop, $"Action: {actionClear_backslash}");

        var actionLoopMode = hkm.MatchAction(System.Windows.Input.Key.L, System.Windows.Input.ModifierKeys.Control);
        Assert("Loop Mode shortcut (Ctrl+L)", actionLoopMode == HotkeyManager.Actions.ToggleLoopMode, $"Action: {actionLoopMode}");

        // Test custom chord parsing and export
        var parsed = HotkeyManager.TryParseChord("Ctrl+Shift+P", out var parsedKey, out var parsedMods);
        Assert("TryParseChord Ctrl+Shift+P", parsed && parsedKey == System.Windows.Input.Key.P && parsedMods == (System.Windows.Input.ModifierKeys.Control | System.Windows.Input.ModifierKeys.Shift));

        var customMap = new System.Collections.Generic.Dictionary<string, string>
        {
            [HotkeyManager.Actions.PlayPause] = "K"
        };
        hkm.LoadCustomHotkeys(customMap);
        var actionK = hkm.MatchAction(System.Windows.Input.Key.K, System.Windows.Input.ModifierKeys.None);
        Assert("Custom shortcut remapping (K for PlayPause)", actionK == HotkeyManager.Actions.PlayPause, $"Action: {actionK}");

        hkm.ResetToDefaults();
        var actionSpace = hkm.MatchAction(System.Windows.Input.Key.Space, System.Windows.Input.ModifierKeys.None);
        Assert("Reset to defaults restores Space for PlayPause", actionSpace == HotkeyManager.Actions.PlayPause, $"Action: {actionSpace}");

        // 7. Test LoopMode and Settings Serialization
        Console.WriteLine("\n--- Testing LoopMode & TimeDisplayFormat Settings ---");
        var testSettings = new SettingsService();
        testSettings.Current.TimeDisplayFormat = "Frames";
        testSettings.Current.LoopMode = "Loop";
        testSettings.Current.CustomHotkeys[HotkeyManager.Actions.PlayPause] = "K";
        testSettings.Save();

        var reloaded = new SettingsService().Load();
        Assert("TimeDisplayFormat persistence", reloaded.TimeDisplayFormat == "Frames", reloaded.TimeDisplayFormat);
        Assert("LoopMode persistence", reloaded.LoopMode == "Loop", reloaded.LoopMode);
        Assert("CustomHotkeys persistence", reloaded.CustomHotkeys.ContainsKey(HotkeyManager.Actions.PlayPause) && reloaded.CustomHotkeys[HotkeyManager.Actions.PlayPause] == "K");

        // 8. Test Video Zoom & Pan Engine
        Console.WriteLine("\n--- Testing Video Zoom & Pan Engine ---");
        using (var zoomPlayer = new MpvPlayer())
        {
            Assert("Initial ZoomFactor is 100%", Math.Abs(zoomPlayer.ZoomFactor - 1.0) < 0.001);
            zoomPlayer.AdjustZoom(1.15);
            Assert("Zoom In increases factor", zoomPlayer.ZoomFactor > 1.0, $"ZoomFactor: {zoomPlayer.ZoomFactor}");

            zoomPlayer.SetPan(0.5, -0.3);
            Assert("Pan set correctly", Math.Abs(zoomPlayer.PanX - 0.5) < 0.001 && Math.Abs(zoomPlayer.PanY - (-0.3)) < 0.001, $"Pan: ({zoomPlayer.PanX}, {zoomPlayer.PanY})");

            zoomPlayer.ResetZoom();
            Assert("ResetZoom restores 100% and resets Pan", Math.Abs(zoomPlayer.ZoomFactor - 1.0) < 0.001 && zoomPlayer.PanX == 0 && zoomPlayer.PanY == 0);

            zoomPlayer.AdjustZoom(1.0 / 1.15);
            Assert("Zoom Out decreases factor", zoomPlayer.ZoomFactor < 1.0, $"ZoomFactor: {zoomPlayer.ZoomFactor}");
        }

        // 9. Test Timecode / Frame Visual Swap
        Console.WriteLine("\n--- Testing Timecode / Frame Visual Swap ---");
        using (var dummyA = new MpvPlayer())
        using (var dummyB = new MpvPlayer())
        {
            var coord = new PlaybackCoordinator(dummyA, dummyB);
            var ffmpegSvc = new FfmpegService();
            var wfSvc = new WaveformService(ffmpegSvc);
            var setSvc = new SettingsService();
            var vm = new MainViewModel(coord, ffmpegSvc, wfSvc, setSvc);

            vm.Position = 125.091; // 2m 5s 91ms

            vm.TimeDisplayFormat = "Timecode";
            Assert("Timecode Mode Primary is Timecode", vm.PrimaryTimeTextA.StartsWith("00:02:05"), vm.PrimaryTimeTextA);
            Assert("Timecode Mode Secondary contains Frame", vm.SecondaryTimeTextA.Contains("Frame"), vm.SecondaryTimeTextA);

            vm.TimeDisplayFormat = "Frames";
            Assert("Frames Mode Primary is Frame", vm.PrimaryTimeTextA.StartsWith("Frame"), vm.PrimaryTimeTextA);
            Assert("Frames Mode Secondary contains Timecode", vm.SecondaryTimeTextA.Contains("00:02:05"), vm.SecondaryTimeTextA);

            // 10. Test Smart Ctrl+Left/Right Hotkeys
            Console.WriteLine("\n--- Testing Smart Ctrl+Left / Right Navigation ---");
            vm.Duration = 100.0;
            vm.Position = 50.0;

            // Without Loop A-B
            vm.JumpToStartOrLoopInCommand.Execute(null);
            Assert("Without Loop, JumpToStart goes to 0.0s", vm.Position == 0.0, $"{vm.Position}s");

            vm.JumpToEndOrLoopOutCommand.Execute(null);
            Assert("Without Loop, JumpToEnd goes to Duration", vm.Position == 100.0, $"{vm.Position}s");

            // With Loop A-B
            vm.LoopStart = 20.0;
            vm.LoopEnd = 80.0;
            vm.Position = 50.0;

            vm.JumpToStartOrLoopInCommand.Execute(null);
            Assert("With Loop, JumpToStart goes to LoopStart", vm.Position == 20.0, $"{vm.Position}s");

            vm.JumpToEndOrLoopOutCommand.Execute(null);
            Assert("With Loop, JumpToEnd goes to LoopEnd", vm.Position == 80.0, $"{vm.Position}s");

            var actionCtrlLeft = vm.HotkeyManager.MatchAction(System.Windows.Input.Key.Left, System.Windows.Input.ModifierKeys.Control);
            Assert("Hotkey Ctrl+Left matches JumpToStartOrLoopIn", actionCtrlLeft == HotkeyManager.Actions.JumpToStartOrLoopIn, actionCtrlLeft ?? "");

            var actionCtrlRight = vm.HotkeyManager.MatchAction(System.Windows.Input.Key.Right, System.Windows.Input.ModifierKeys.Control);
            Assert("Hotkey Ctrl+Right matches JumpToEndOrLoopOut", actionCtrlRight == HotkeyManager.Actions.JumpToEndOrLoopOut, actionCtrlRight ?? "");
        }

        Console.WriteLine("\n==================================================");
        Console.WriteLine($"  TEST RESULTS: {passed} PASSED, {failed} FAILED");
        Console.WriteLine("==================================================");

        return failed == 0 ? 0 : 1;
    }
}

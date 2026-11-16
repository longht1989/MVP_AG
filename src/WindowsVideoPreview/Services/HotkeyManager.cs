using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;
using WindowsVideoPreview.Models;

namespace WindowsVideoPreview.Services;

public class HotkeyManager
{
    private readonly List<HotkeyBinding> _bindings = new();

    public static class Actions
    {
        public const string PlayPause = "PlayPause";
        public const string FrameStepForward = "FrameStepForward";
        public const string FrameStepBackward = "FrameStepBackward";
        public const string SeekForward1s = "SeekForward1s";
        public const string SeekBackward1s = "SeekBackward1s";
        public const string SeekForward5s = "SeekForward5s";
        public const string SeekBackward5s = "SeekBackward5s";
        public const string SetLoopA = "SetLoopA";
        public const string SetLoopB = "SetLoopB";
        public const string ClearLoop = "ClearLoop";
        public const string ToggleLoopMode = "ToggleLoopMode";
        public const string ToggleFullscreen = "ToggleFullscreen";
        public const string ToggleWaveform = "ToggleWaveform";
        public const string TogglePlaylist = "TogglePlaylist";
        public const string ToggleInspector = "ToggleInspector";
        public const string ToggleMute = "ToggleMute";
        public const string OpenFileA = "OpenFileA";
        public const string OpenFileB = "OpenFileB";
        public const string JumpToTimecode = "JumpToTimecode";
        public const string JumpToStartOrLoopIn = "JumpToStartOrLoopIn";
        public const string JumpToEndOrLoopOut = "JumpToEndOrLoopOut";
        public const string FlipWipe = "FlipWipe";
    }

    public HotkeyManager()
    {
        InitializeDefaultBindings();
    }

    private void InitializeDefaultBindings()
    {
        _bindings.Clear();

        // Playback
        AddBinding(Actions.PlayPause, "Play / Pause", "Playback", Key.Space, ModifierKeys.None);
        AddBinding(Actions.ToggleLoopMode, "Toggle Loop Mode (Once / Loop)", "Playback", Key.L, ModifierKeys.Control);

        // Navigation
        AddBinding(Actions.FrameStepBackward, "Step 1 Frame Backward", "Navigation", Key.Left, ModifierKeys.None);
        AddBinding(Actions.FrameStepForward, "Step 1 Frame Forward", "Navigation", Key.Right, ModifierKeys.None);
        AddBinding(Actions.SeekBackward1s, "Seek Backward 1s", "Navigation", Key.Left, ModifierKeys.Shift);
        AddBinding(Actions.SeekForward1s, "Seek Forward 1s", "Navigation", Key.Right, ModifierKeys.Shift);
        AddBinding(Actions.JumpToStartOrLoopIn, "Jump to Start or Loop In (A)", "Navigation", Key.Left, ModifierKeys.Control);
        AddBinding(Actions.JumpToEndOrLoopOut, "Jump to End or Loop Out (B)", "Navigation", Key.Right, ModifierKeys.Control);
        AddBinding(Actions.JumpToTimecode, "Jump to Timecode / Frame", "Navigation", Key.G, ModifierKeys.Control);

        // Loop A-B (Default: , for A, . for B, / for Clear)
        AddBinding(Actions.SetLoopA, "Set Loop In (Start A)", "Loop", Key.OemComma, ModifierKeys.None);
        AddBinding(Actions.SetLoopB, "Set Loop Out (End B)", "Loop", Key.OemPeriod, ModifierKeys.None);
        AddBinding(Actions.ClearLoop, "Clear Loop A-B", "Loop", Key.OemQuestion, ModifierKeys.None);

        // Media & Files
        AddBinding(Actions.OpenFileA, "Open Video A", "Media", Key.O, ModifierKeys.Control);
        AddBinding(Actions.OpenFileB, "Open Video B", "Media", Key.O, ModifierKeys.Control | ModifierKeys.Shift);

        // View & Audio
        AddBinding(Actions.ToggleFullscreen, "Toggle Fullscreen", "View", Key.F, ModifierKeys.None);
        AddBinding(Actions.ToggleWaveform, "Toggle Waveform", "View", Key.W, ModifierKeys.None);
        AddBinding(Actions.TogglePlaylist, "Toggle Playlist", "View", Key.P, ModifierKeys.None);
        AddBinding(Actions.ToggleInspector, "Toggle Inspector", "View", Key.I, ModifierKeys.None);
        AddBinding(Actions.ToggleMute, "Toggle Mute", "Audio", Key.M, ModifierKeys.None);
        AddBinding(Actions.FlipWipe, "Quick Flip / Overlay Wipe", "View", Key.Tab, ModifierKeys.None);
    }

    private void AddBinding(string id, string name, string category, Key key, ModifierKeys mods)
    {
        _bindings.Add(new HotkeyBinding
        {
            ActionId = id,
            DisplayName = name,
            Category = category,
            Key = key,
            Modifiers = mods,
            DefaultKey = key,
            DefaultModifiers = mods
        });
    }

    public IReadOnlyList<HotkeyBinding> Bindings => _bindings;

    public void LoadCustomHotkeys(Dictionary<string, string>? customMap)
    {
        if (customMap == null || customMap.Count == 0) return;

        foreach (var binding in _bindings)
        {
            if (customMap.TryGetValue(binding.ActionId, out var chord) && !string.IsNullOrWhiteSpace(chord))
            {
                if (TryParseChord(chord, out Key key, out ModifierKeys mods))
                {
                    binding.Key = key;
                    binding.Modifiers = mods;
                }
            }
        }
    }

    public Dictionary<string, string> ExportCustomHotkeys()
    {
        var dict = new Dictionary<string, string>();
        foreach (var b in _bindings)
        {
            if (b.Key != b.DefaultKey || b.Modifiers != b.DefaultModifiers)
            {
                dict[b.ActionId] = SerializeChord(b.Key, b.Modifiers);
            }
        }
        return dict;
    }

    public void ResetToDefaults()
    {
        foreach (var b in _bindings)
        {
            b.Key = b.DefaultKey;
            b.Modifiers = b.DefaultModifiers;
        }
    }

    public string? MatchAction(Key key, ModifierKeys modifiers)
    {
        // Special case: ClearLoop also accepts Divide (Numpad /) and OemBackslash (\) by default
        var bClear = _bindings.FirstOrDefault(b => b.ActionId == Actions.ClearLoop);
        if (bClear != null && (key == Key.Divide || key == Key.OemBackslash) && modifiers == ModifierKeys.None)
        {
            if (bClear.Key == Key.OemQuestion && bClear.Modifiers == ModifierKeys.None)
            {
                return Actions.ClearLoop;
            }
        }

        foreach (var b in _bindings)
        {
            if (b.Key == key && b.Modifiers == modifiers)
            {
                return b.ActionId;
            }
        }
        return null;
    }

    public string GetShortcutDisplay(string actionId)
    {
        var b = _bindings.FirstOrDefault(x => x.ActionId == actionId);
        return b != null ? b.KeyDisplay : string.Empty;
    }

    public static string SerializeChord(Key key, ModifierKeys modifiers)
    {
        if (key == Key.None) return "None";
        var parts = new List<string>();
        if ((modifiers & ModifierKeys.Control) == ModifierKeys.Control) parts.Add("Ctrl");
        if ((modifiers & ModifierKeys.Alt) == ModifierKeys.Alt) parts.Add("Alt");
        if ((modifiers & ModifierKeys.Shift) == ModifierKeys.Shift) parts.Add("Shift");
        if ((modifiers & ModifierKeys.Windows) == ModifierKeys.Windows) parts.Add("Win");
        parts.Add(key.ToString());
        return string.Join("+", parts);
    }

    public static bool TryParseChord(string chord, out Key key, out ModifierKeys modifiers)
    {
        key = Key.None;
        modifiers = ModifierKeys.None;

        if (string.IsNullOrWhiteSpace(chord) || chord.Equals("None", StringComparison.OrdinalIgnoreCase))
            return false;

        var tokens = chord.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0) return false;

        string keyStr = tokens[^1];
        if (!Enum.TryParse(keyStr, true, out key))
        {
            // Fallback for special symbols
            key = keyStr.ToLowerInvariant() switch
            {
                "," => Key.OemComma,
                "." => Key.OemPeriod,
                "/" => Key.OemQuestion,
                "[" => Key.OemOpenBrackets,
                "]" => Key.OemCloseBrackets,
                "\\" => Key.OemBackslash,
                "-" => Key.OemMinus,
                "+" => Key.OemPlus,
                "space" => Key.Space,
                "esc" => Key.Escape,
                "tab" => Key.Tab,
                "enter" => Key.Return,
                _ => Key.None
            };
            if (key == Key.None) return false;
        }

        for (int i = 0; i < tokens.Length - 1; i++)
        {
            var mod = tokens[i].ToLowerInvariant();
            if (mod == "ctrl" || mod == "control") modifiers |= ModifierKeys.Control;
            else if (mod == "alt") modifiers |= ModifierKeys.Alt;
            else if (mod == "shift") modifiers |= ModifierKeys.Shift;
            else if (mod == "win" || mod == "windows") modifiers |= ModifierKeys.Windows;
        }

        return true;
    }
}

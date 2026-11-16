using System;
using System.ComponentModel;
using System.Windows.Input;

namespace WindowsVideoPreview.Models;

public class HotkeyBinding : INotifyPropertyChanged
{
    private Key _key;
    private ModifierKeys _modifiers;

    public string ActionId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Category { get; set; } = "Playback"; // Playback, Loop, Navigation, View, Media

    public Key Key
    {
        get => _key;
        set
        {
            if (_key != value)
            {
                _key = value;
                OnPropertyChanged(nameof(Key));
                OnPropertyChanged(nameof(KeyDisplay));
            }
        }
    }

    public ModifierKeys Modifiers
    {
        get => _modifiers;
        set
        {
            if (_modifiers != value)
            {
                _modifiers = value;
                OnPropertyChanged(nameof(Modifiers));
                OnPropertyChanged(nameof(KeyDisplay));
            }
        }
    }

    public Key DefaultKey { get; set; }
    public ModifierKeys DefaultModifiers { get; set; }

    public string KeyDisplay => FormatDisplay(Key, Modifiers);

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public static string FormatDisplay(Key key, ModifierKeys modifiers)
    {
        if (key == Key.None) return "None";

        var parts = new System.Collections.Generic.List<string>();
        if ((modifiers & ModifierKeys.Control) == ModifierKeys.Control) parts.Add("Ctrl");
        if ((modifiers & ModifierKeys.Alt) == ModifierKeys.Alt) parts.Add("Alt");
        if ((modifiers & ModifierKeys.Shift) == ModifierKeys.Shift) parts.Add("Shift");
        if ((modifiers & ModifierKeys.Windows) == ModifierKeys.Windows) parts.Add("Win");

        string keyName = key switch
        {
            Key.Space => "Space",
            Key.Left => "←",
            Key.Right => "→",
            Key.Up => "↑",
            Key.Down => "↓",
            Key.OemComma => ",",
            Key.OemPeriod => ".",
            Key.OemQuestion => "/",
            Key.OemOpenBrackets => "[",
            Key.OemCloseBrackets => "]",
            Key.OemBackslash => "\\",
            Key.OemMinus => "-",
            Key.OemPlus => "+",
            Key.Escape => "Esc",
            Key.Tab => "Tab",
            Key.Return => "Enter",
            _ => key.ToString()
        };

        parts.Add(keyName);
        return string.Join("+", parts);
    }
}

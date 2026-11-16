using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Button = System.Windows.Controls.Button;
using RadioButton = System.Windows.Controls.RadioButton;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using WindowsVideoPreview.Models;
using WindowsVideoPreview.Services;

namespace WindowsVideoPreview.Views;

public partial class HotkeySettingsDialog : Window
{
    private readonly HotkeyManager _manager;
    private readonly List<HotkeyBinding> _workingCopy = new();
    private HotkeyBinding? _recordingBinding;
    private Button? _recordingButton;
    private string _activeCategory = "All";

    public HotkeySettingsDialog(HotkeyManager manager)
    {
        InitializeComponent();
        _manager = manager;

        // Clone current state for safe editing
        foreach (var b in _manager.Bindings)
        {
            _workingCopy.Add(new HotkeyBinding
            {
                ActionId = b.ActionId,
                DisplayName = b.DisplayName,
                Category = b.Category,
                Key = b.Key,
                Modifiers = b.Modifiers,
                DefaultKey = b.DefaultKey,
                DefaultModifiers = b.DefaultModifiers
            });
        }

        RefreshList();
        PreviewKeyDown += OnDialogPreviewKeyDown;
    }

    private void RefreshList()
    {
        var filtered = _activeCategory == "All"
            ? _workingCopy
            : _workingCopy.Where(b => b.Category.Equals(_activeCategory, StringComparison.OrdinalIgnoreCase)).ToList();

        HotkeysListView.ItemsSource = filtered;
    }

    private void OnFilterChanged(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton rb && rb.Content is string cat)
        {
            _activeCategory = cat;
            RefreshList();
        }
    }

    private void OnShortcutButtonClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is HotkeyBinding binding)
        {
            if (_recordingBinding != null && _recordingButton != null)
            {
                // Cancel previous recording
                _recordingButton.Content = _recordingBinding.KeyDisplay;
            }

            _recordingBinding = binding;
            _recordingButton = btn;
            btn.Content = "Press keys...";
            StatusInfoText.Text = $"Recording new shortcut for '{binding.DisplayName}'. Press any key combination (or Esc to cancel)...";
            StatusInfoText.Foreground = (Brush)FindResource("BrushAccent");
        }
    }

    private void OnDialogPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_recordingBinding == null || _recordingButton == null) return;

        // Ignore pure modifier presses
        Key key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.LeftCtrl || key == Key.RightCtrl ||
            key == Key.LeftAlt || key == Key.RightAlt ||
            key == Key.LeftShift || key == Key.RightShift ||
            key == Key.LWin || key == Key.RWin)
        {
            return;
        }

        e.Handled = true;

        if (key == Key.Escape)
        {
            // Cancel recording
            _recordingButton.Content = _recordingBinding.KeyDisplay;
            _recordingBinding = null;
            _recordingButton = null;
            StatusInfoText.Text = "Shortcut recording cancelled.";
            StatusInfoText.Foreground = (Brush)FindResource("BrushTextSecondary");
            return;
        }

        ModifierKeys modifiers = Keyboard.Modifiers;

        // Check for conflicts
        var conflict = _workingCopy.FirstOrDefault(b => b != _recordingBinding && b.Key == key && b.Modifiers == modifiers);
        if (conflict != null)
        {
            StatusInfoText.Text = $"Warning: '{HotkeyBinding.FormatDisplay(key, modifiers)}' was already used by '{conflict.DisplayName}'. Conflict cleared for '{conflict.DisplayName}'.";
            StatusInfoText.Foreground = Brushes.Orange;
            conflict.Key = Key.None;
            conflict.Modifiers = ModifierKeys.None;
        }
        else
        {
            StatusInfoText.Text = $"Assigned '{HotkeyBinding.FormatDisplay(key, modifiers)}' to '{_recordingBinding.DisplayName}'.";
            StatusInfoText.Foreground = (Brush)FindResource("BrushTextSecondary");
        }

        _recordingBinding.Key = key;
        _recordingBinding.Modifiers = modifiers;
        _recordingButton.Content = _recordingBinding.KeyDisplay;

        _recordingBinding = null;
        _recordingButton = null;
    }

    private void OnResetSingleClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is HotkeyBinding binding)
        {
            binding.Key = binding.DefaultKey;
            binding.Modifiers = binding.DefaultModifiers;
            StatusInfoText.Text = $"Reset '{binding.DisplayName}' to default ({binding.KeyDisplay}).";
            StatusInfoText.Foreground = (Brush)FindResource("BrushTextSecondary");
        }
    }

    private void OnResetAllClick(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show(
            "Are you sure you want to reset all keyboard shortcuts to their factory defaults?",
            "Reset Shortcuts",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result == MessageBoxResult.Yes)
        {
            foreach (var b in _workingCopy)
            {
                b.Key = b.DefaultKey;
                b.Modifiers = b.DefaultModifiers;
            }
            RefreshList();
            StatusInfoText.Text = "All shortcuts have been reset to factory defaults.";
            StatusInfoText.Foreground = (Brush)FindResource("BrushTextSecondary");
        }
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        // Apply working copy back to manager
        foreach (var working in _workingCopy)
        {
            var target = _manager.Bindings.FirstOrDefault(b => b.ActionId == working.ActionId);
            if (target != null)
            {
                target.Key = working.Key;
                target.Modifiers = working.Modifiers;
            }
        }

        DialogResult = true;
        Close();
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}

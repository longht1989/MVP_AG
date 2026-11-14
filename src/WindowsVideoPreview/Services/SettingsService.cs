using System;
using System.IO;
using System.Text.Json;
using WindowsVideoPreview.Models;

namespace WindowsVideoPreview.Services;

public class SettingsService
{
    private readonly string _settingsFilePath;
    private AppSettings _settings;

    public AppSettings Current => _settings;

    public SettingsService()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var dir = Path.Combine(appData, "WindowsVideoPreview");
        Directory.CreateDirectory(dir);
        _settingsFilePath = Path.Combine(dir, "settings.json");
        _settings = Load();
    }

    public AppSettings Load()
    {
        try
        {
            if (File.Exists(_settingsFilePath))
            {
                var json = File.ReadAllText(_settingsFilePath);
                var loaded = JsonSerializer.Deserialize<AppSettings>(json);
                if (loaded != null)
                {
                    _settings = loaded;
                    return _settings;
                }
            }
        }
        catch { }

        _settings = new AppSettings();
        return _settings;
    }

    public void Save()
    {
        try
        {
            var options = new JsonSerializerOptions { WriteIndented = true };
            var json = JsonSerializer.Serialize(_settings, options);
            File.WriteAllText(_settingsFilePath, json);
        }
        catch { }
    }
}

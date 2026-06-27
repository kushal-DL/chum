using System.IO;
using System.Text.Json;
using Chum.App.Models;

namespace Chum.App.Services;

/// <summary>Persists AppSettings as JSON in %APPDATA%\Chum\settings.json.</summary>
public sealed class SettingsService
{
    private static readonly string SettingsDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Chum");
    private static readonly string SettingsPath = Path.Combine(SettingsDir, "settings.json");

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private AppSettings _current = new();
    public AppSettings Current => _current;

    public event EventHandler? SettingsChanged;

    public void Load()
    {
        try
        {
            if (!File.Exists(SettingsPath)) { Save(); return; }
            var json = File.ReadAllText(SettingsPath);
            _current = JsonSerializer.Deserialize<AppSettings>(json, JsonOpts) ?? new AppSettings();
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "Failed to load settings; using defaults");
            _current = new AppSettings();
        }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(SettingsDir);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(_current, JsonOpts));
            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Failed to save settings");
        }
    }

    public void Update(Action<AppSettings> mutate)
    {
        mutate(_current);
        Save();
    }
}

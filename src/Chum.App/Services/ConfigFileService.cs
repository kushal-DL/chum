using System.IO;
using System.Text.Json;
using Serilog;

namespace Chum.App.Services;

/// <summary>
/// Stores API keys in config.json next to the executable.
/// The installer grants BUILTIN\Users write access to this file.
/// </summary>
public sealed class ConfigFileService
{
    private static string ConfigPath =>
        Path.Combine(AppContext.BaseDirectory, "config.json");

    private ConfigData _data = new();

    public void Load()
    {
        if (!File.Exists(ConfigPath)) return;
        try
        {
            var json = File.ReadAllText(ConfigPath);
            _data = JsonSerializer.Deserialize<ConfigData>(json, JsonOptions) ?? new();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Could not read config.json — using defaults");
        }
    }

    public string? AnthropicApiKey
    {
        get => string.IsNullOrWhiteSpace(_data.AnthropicApiKey) ? null : _data.AnthropicApiKey;
        set
        {
            _data = _data with { AnthropicApiKey = value?.Trim() ?? string.Empty };
            TrySave();
        }
    }

    public string? OpenAiApiKey
    {
        get => string.IsNullOrWhiteSpace(_data.OpenAiApiKey) ? null : _data.OpenAiApiKey;
        set
        {
            _data = _data with { OpenAiApiKey = value?.Trim() ?? string.Empty };
            TrySave();
        }
    }

    public string? CloudSttApiKey
    {
        get => string.IsNullOrWhiteSpace(_data.CloudSttApiKey) ? null : _data.CloudSttApiKey;
        set
        {
            _data = _data with { CloudSttApiKey = value?.Trim() ?? string.Empty };
            TrySave();
        }
    }

    private void TrySave()
    {
        try
        {
            File.WriteAllText(ConfigPath, JsonSerializer.Serialize(_data, JsonOptions));
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to write config.json at {Path} — run installer to grant write access", ConfigPath);
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private record ConfigData(
        string AnthropicApiKey = "",
        string OpenAiApiKey = "",
        string CloudSttApiKey = "");
}

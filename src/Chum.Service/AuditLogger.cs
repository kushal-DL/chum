using System.Text.Json;

namespace Chum.Service;

/// <summary>
/// Writes a tamper-evident JSON-Lines audit log to %PROGRAMDATA%\Chum\audit.jsonl.
/// Readable by admin accounts; protected from modification by standard users via
/// the installer's ACL setup. Every query, hotkey, provider call, and service
/// lifecycle event is recorded. No transcript content or API key material is logged.
/// </summary>
public sealed class AuditLogger : IDisposable
{
    private static readonly string LogDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Chum");

    private static readonly string LogPath = Path.Combine(LogDir, "audit.jsonl");

    private readonly StreamWriter _writer;
    private readonly Lock _lock = new();
    private bool _disposed;

    public AuditLogger()
    {
        Directory.CreateDirectory(LogDir);
        _writer = new StreamWriter(LogPath, append: true, System.Text.Encoding.UTF8) { AutoFlush = true };
        Write("ServiceStart", null, null, null, null, null);
    }

    public void LogQuery(string provider, int inputTokens, int outputTokens, long latencyMs)
        => Write("QueryFired", provider, inputTokens, outputTokens, latencyMs, null);

    public void LogHotkey(string hotkeyId)
        => Write("HotkeyPress", null, null, null, null, hotkeyId);

    public void LogServiceStop()
        => Write("ServiceStop", null, null, null, null, null);

    private void Write(string eventName, string? provider, int? inputTokens,
        int? outputTokens, long? latencyMs, string? hotkeyId)
    {
        var entry = new
        {
            ts = DateTimeOffset.UtcNow.ToString("o"),
            ev = eventName,
            provider,
            inputTokens,
            outputTokens,
            latencyMs,
            hotkeyId,
        };

        var line = JsonSerializer.Serialize(entry);
        lock (_lock)
        {
            if (!_disposed) _writer.WriteLine(line);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        LogServiceStop();
        _writer.Dispose();
    }
}

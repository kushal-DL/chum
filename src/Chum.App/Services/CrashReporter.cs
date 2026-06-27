using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Serilog;

namespace Chum.App.Services;

/// <summary>
/// Opt-in crash reporter. Collects structured crash info and writes it to a local
/// JSON-Lines crash dump file in %LOCALAPPDATA%\Chum\CrashReports\.
/// Never uploads data automatically — the user must opt in and can review the file
/// before sharing it with support.
/// </summary>
public static class CrashReporter
{
    private static readonly string CrashDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Chum", "CrashReports");

    /// <summary>
    /// Writes a crash report to disk and returns the path of the written file.
    /// Never throws — safe to call from exception handlers.
    /// </summary>
    public static string? TryWriteReport(Exception ex, string? transcriptSummary = null)
    {
        try
        {
            Directory.CreateDirectory(CrashDir);

            var report = BuildReport(ex, transcriptSummary);
            var fileName = $"crash_{DateTime.UtcNow:yyyyMMdd_HHmmss}_{report.SessionId[..8]}.json";
            var filePath = Path.Combine(CrashDir, fileName);

            var json = JsonSerializer.Serialize(report, new JsonSerializerOptions
            {
                WriteIndented = true,
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
            });
            File.WriteAllText(filePath, json, Encoding.UTF8);

            Log.Information("Crash report written: {Path}", filePath);
            return filePath;
        }
        catch (Exception writeEx)
        {
            Log.Error(writeEx, "CrashReporter: failed to write crash report");
            return null;
        }
    }

    /// <summary>
    /// Returns the directory where crash reports are stored.
    /// </summary>
    public static string CrashReportDirectory => CrashDir;

    /// <summary>
    /// Returns the paths of all crash reports written in this installation,
    /// sorted newest-first. Returns empty array if none exist or directory missing.
    /// </summary>
    public static string[] GetRecentReports(int max = 10)
    {
        if (!Directory.Exists(CrashDir)) return [];
        return Directory.GetFiles(CrashDir, "crash_*.json")
            .OrderByDescending(f => f)
            .Take(max)
            .ToArray();
    }

    private static CrashReport BuildReport(Exception ex, string? transcriptSummary)
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown";
        var osVer = RuntimeInformation.OSDescription;
        var memMb = (int)(Process.GetCurrentProcess().WorkingSet64 / (1024 * 1024));

        return new CrashReport(
            SessionId: Guid.NewGuid().ToString("N"),
            Timestamp: DateTimeOffset.UtcNow,
            ChumVersion: version,
            OsVersion: osVer,
            DotNetVersion: RuntimeInformation.FrameworkDescription,
            WorkingSetMb: memMb,
            ExceptionType: ex.GetType().FullName ?? ex.GetType().Name,
            ExceptionMessage: ex.Message,
            StackTrace: ex.StackTrace,
            InnerException: ex.InnerException is null ? null : $"{ex.InnerException.GetType().Name}: {ex.InnerException.Message}",
            TranscriptLineSummary: transcriptSummary);
    }

    private sealed record CrashReport(
        string SessionId,
        DateTimeOffset Timestamp,
        string ChumVersion,
        string OsVersion,
        string DotNetVersion,
        int WorkingSetMb,
        string ExceptionType,
        string ExceptionMessage,
        string? StackTrace,
        string? InnerException,
        string? TranscriptLineSummary);
}

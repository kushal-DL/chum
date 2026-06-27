using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Reflection;
using System.Diagnostics;
using System.IO;
using Serilog;

namespace Chum.App.Services;

/// <summary>
/// Checks GitHub Releases API for newer versions of Chum and facilitates download + install.
/// Check policy: always, daily (default), or never — stored in AppSettings.
/// Download is blocked while audio capture is active.
/// </summary>
public sealed class UpdateChecker
{
    private const string GithubReleasesUrl =
        "https://api.github.com/repos/kushal-DL/chum/releases/latest";
    private const string UserAgent = "Chum-AutoUpdater/1.0";

    private readonly HttpClient _http;

    public UpdateChecker(HttpClient http)
    {
        _http = http;
        _http.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
        // GitHub API requires Accept header for JSON
        _http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
    }

    /// <summary>
    /// Queries the GitHub Releases API and compares the latest tag against the running assembly version.
    /// Returns an UpdateInfo if a newer version is available, or null if up-to-date or check fails.
    /// Never throws — all exceptions are caught and logged.
    /// </summary>
    public async Task<UpdateInfo?> CheckForUpdateAsync(CancellationToken ct = default)
    {
        try
        {
            var release = await _http.GetFromJsonAsync<GithubRelease>(GithubReleasesUrl, ct);
            if (release is null) return null;

            var latestVersion = ParseVersion(release.TagName);
            var currentVersion = GetCurrentVersion();

            if (latestVersion is null || currentVersion is null) return null;
            if (latestVersion <= currentVersion)
            {
                Log.Information("UpdateChecker: already on latest version {Ver}", currentVersion);
                return null;
            }

            // Find the MSI/installer asset
            var installerAsset = release.Assets
                ?.FirstOrDefault(a => a.Name?.EndsWith(".msi", StringComparison.OrdinalIgnoreCase) == true
                                   || a.Name?.EndsWith("-setup.exe", StringComparison.OrdinalIgnoreCase) == true);

            Log.Information("UpdateChecker: new version {Latest} available (current: {Current})",
                latestVersion, currentVersion);

            return new UpdateInfo(
                Version: latestVersion.ToString(),
                ReleaseNotes: release.Body?.Split('\n').FirstOrDefault()?.Trim() ?? string.Empty,
                DownloadUrl: installerAsset?.BrowserDownloadUrl,
                InstallerFileName: installerAsset?.Name,
                Sha256: release.Body is not null ? ParseSha256FromBody(release.Body, installerAsset?.Name) : null);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            Log.Warning(ex, "UpdateChecker: update check failed");
            return null;
        }
    }

    /// <summary>
    /// Downloads the installer from <paramref name="info"/>, verifies SHA256 if known,
    /// and launches it. Does NOT close Chum — the installer handles that.
    /// Returns the path to the downloaded installer, or null on failure.
    /// </summary>
    public async Task<string?> DownloadAndLaunchAsync(UpdateInfo info, CancellationToken ct = default)
    {
        if (info.DownloadUrl is null)
        {
            Log.Warning("UpdateChecker: no installer URL in release — cannot auto-update");
            return null;
        }

        var tempDir = Path.GetTempPath();
        var localPath = Path.Combine(tempDir, info.InstallerFileName ?? "chum-setup.msi");

        try
        {
            Log.Information("UpdateChecker: downloading installer from {Url}", info.DownloadUrl);
            var bytes = await _http.GetByteArrayAsync(info.DownloadUrl, ct);

            // SHA256 verification (if checksum was published in the release body)
            if (info.Sha256 is not null)
            {
                var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
                if (!hash.Equals(info.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    Log.Error("UpdateChecker: SHA256 mismatch — expected {Expected}, got {Got}",
                        info.Sha256, hash);
                    return null;
                }
                Log.Information("UpdateChecker: SHA256 verified OK");
            }

            await File.WriteAllBytesAsync(localPath, bytes, ct);
            Log.Information("UpdateChecker: installer saved to {Path}", localPath);

            // Launch installer — MSI: msiexec /i, EXE: direct
            var args = localPath.EndsWith(".msi", StringComparison.OrdinalIgnoreCase)
                ? $"/i \"{localPath}\" /passive"
                : "/S"; // NSIS/Inno silent flag

            var startInfo = localPath.EndsWith(".msi", StringComparison.OrdinalIgnoreCase)
                ? new ProcessStartInfo("msiexec.exe", args) { UseShellExecute = true }
                : new ProcessStartInfo(localPath, args)      { UseShellExecute = true };

            Process.Start(startInfo);
            Log.Information("UpdateChecker: installer launched — Chum will be restarted by installer");
            return localPath;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            Log.Error(ex, "UpdateChecker: download/launch failed");
            return null;
        }
    }

    private static Version? GetCurrentVersion()
    {
        var v = Assembly.GetExecutingAssembly().GetName().Version;
        return v is null ? null : new Version(v.Major, v.Minor, v.Build);
    }

    private static Version? ParseVersion(string? tag)
    {
        if (tag is null) return null;
        // Accept tags like "v1.2.3", "1.2.3", "v1.2"
        var cleaned = tag.TrimStart('v', 'V');
        return Version.TryParse(cleaned, out var v) ? v : null;
    }

    // Looks for lines like "SHA256: abc123...  chum-1.2.msi" in the release body.
    private static string? ParseSha256FromBody(string body, string? fileName)
    {
        if (fileName is null) return null;
        foreach (var line in body.Split('\n'))
        {
            if (!line.Contains(fileName, StringComparison.OrdinalIgnoreCase)) continue;
            // Hexadecimal SHA256 is 64 characters
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var hash = parts.FirstOrDefault(p => p.Length == 64 && IsHex(p));
            if (hash is not null) return hash.ToLowerInvariant();
        }
        return null;
    }

    private static bool IsHex(string s)
    {
        foreach (char c in s)
            if (!Uri.IsHexDigit(c)) return false;
        return true;
    }

    // Minimal GitHub API response shape — only the fields we use
    private sealed class GithubRelease
    {
        public string? TagName { get; set; }
        public string? Body { get; set; }
        public List<GithubAsset>? Assets { get; set; }
    }

    private sealed class GithubAsset
    {
        public string? Name { get; set; }
        public string? BrowserDownloadUrl { get; set; }
    }
}

public record UpdateInfo(
    string Version,
    string ReleaseNotes,
    string? DownloadUrl,
    string? InstallerFileName,
    string? Sha256);

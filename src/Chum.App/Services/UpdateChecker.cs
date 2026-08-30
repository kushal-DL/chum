using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Reflection;
using System.Diagnostics;
using System.IO;
using Serilog;

namespace Chum.App.Services;

/// <summary>
/// Checks GitHub Releases for a newer version and applies the update in one click.
/// Check policy: at most once per day (enforced by the caller via AppSettings.LastUpdateCheckUtc).
/// Never throws — all failures are logged and surfaced via return value.
/// </summary>
public sealed class UpdateChecker
{
    private const string ReleasesApiUrl =
        "https://api.github.com/repos/kushal-DL/chum/releases/latest";

    private readonly HttpClient _http;

    public UpdateChecker(HttpClient http)
    {
        _http = http;
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("Chum-AutoUpdater/1.0");
        _http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
    }

    // -----------------------------------------------------------------------
    // Version check
    // -----------------------------------------------------------------------

    /// <summary>
    /// Queries GitHub Releases and returns update info when a newer version exists.
    /// Returns null when already up-to-date or the check cannot be completed.
    /// </summary>
    public async Task<UpdateInfo?> CheckForUpdateAsync(CancellationToken ct = default)
    {
        try
        {
            var release = await _http.GetFromJsonAsync<GithubRelease>(ReleasesApiUrl, ct);
            if (release is null) return null;

            var latest  = ParseVersion(release.TagName);
            var current = GetCurrentVersion();
            if (latest is null || current is null) return null;

            if (latest <= current)
            {
                Log.Information("UpdateChecker: already on latest ({Ver})", current);
                return null;
            }

            // The release ZIP asset (e.g. chum-0.1.1.zip produced by Publish-Release.ps1)
            var asset = release.Assets?.FirstOrDefault(a =>
                a.Name?.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) == true &&
                a.Name.StartsWith("chum-", StringComparison.OrdinalIgnoreCase));

            Log.Information("UpdateChecker: new version {New} available (have {Now})", latest, current);

            return new UpdateInfo(
                Version:           latest.ToString(),
                ReleaseNotes:      release.Body?.Split('\n').FirstOrDefault()?.Trim() ?? string.Empty,
                DownloadUrl:       asset?.BrowserDownloadUrl,
                ZipFileName:       asset?.Name,
                Sha256:            release.Body is not null
                                   ? ParseSha256(release.Body, asset?.Name)
                                   : null);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            Log.Warning(ex, "UpdateChecker: version check failed");
            return null;
        }
    }

    // -----------------------------------------------------------------------
    // Download + launch
    // -----------------------------------------------------------------------

    /// <summary>
    /// Downloads the release ZIP, extracts it to a temp folder, and launches
    /// install.cmd as Administrator. The caller should exit the app immediately
    /// after this returns true — the installer will stop the service, replace
    /// all files, and restart everything.
    /// Returns false (never throws) if the download or launch fails.
    /// </summary>
    public async Task<bool> DownloadAndLaunchAsync(
        UpdateInfo info,
        IProgress<int>? progress = null,
        CancellationToken ct = default)
    {
        if (info.DownloadUrl is null)
        {
            Log.Warning("UpdateChecker: release has no ZIP asset — cannot auto-update");
            return false;
        }

        var zipPath    = Path.Combine(Path.GetTempPath(), info.ZipFileName ?? "chum-update.zip");
        var extractDir = Path.Combine(Path.GetTempPath(),
            "chum-update-" + info.Version.Replace('.', '-'));

        try
        {
            // --- Download with progress ------------------------------------------
            Log.Information("UpdateChecker: downloading {Url}", info.DownloadUrl);
            using var response = await _http.GetAsync(
                info.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength ?? 0L;
            await using (var fs = new FileStream(
                zipPath, FileMode.Create, FileAccess.Write, FileShare.None, 65536, true))
            await using (var net = await response.Content.ReadAsStreamAsync(ct))
            {
                var buf = new byte[65536];
                long downloaded = 0;
                int  read;
                while ((read = await net.ReadAsync(buf, ct)) > 0)
                {
                    await fs.WriteAsync(buf.AsMemory(0, read), ct);
                    downloaded += read;
                    if (totalBytes > 0)
                        progress?.Report((int)(downloaded * 100 / totalBytes));
                }
            }
            Log.Information("UpdateChecker: download complete ({Bytes} bytes)", new FileInfo(zipPath).Length);

            // --- Optional SHA256 verification ------------------------------------
            if (info.Sha256 is not null)
            {
                var bytes = await File.ReadAllBytesAsync(zipPath, ct);
                var hash  = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
                if (!hash.Equals(info.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    Log.Error("UpdateChecker: SHA256 mismatch — aborting update");
                    return false;
                }
                Log.Information("UpdateChecker: SHA256 OK");
            }

            // --- Extract ---------------------------------------------------------
            if (Directory.Exists(extractDir))
                Directory.Delete(extractDir, recursive: true);
            ZipFile.ExtractToDirectory(zipPath, extractDir);
            Log.Information("UpdateChecker: extracted to {Dir}", extractDir);

            // --- Find install.cmd ------------------------------------------------
            var installCmd = Directory
                .GetFiles(extractDir, "install.cmd", SearchOption.AllDirectories)
                .FirstOrDefault();
            if (installCmd is null)
            {
                Log.Error("UpdateChecker: install.cmd not found in ZIP");
                return false;
            }

            // --- Launch elevated (UAC prompt) ------------------------------------
            Process.Start(new ProcessStartInfo
            {
                FileName       = installCmd,
                Verb           = "runas",       // request admin — triggers UAC
                UseShellExecute = true
            });
            Log.Information("UpdateChecker: install.cmd launched — Chum will restart after install");
            return true;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            Log.Error(ex, "UpdateChecker: download/launch failed");
            return false;
        }
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    public static Version? GetCurrentVersion()
    {
        var v = Assembly.GetExecutingAssembly().GetName().Version;
        return v is null ? null : new Version(v.Major, v.Minor, v.Build);
    }

    private static Version? ParseVersion(string? tag)
    {
        if (tag is null) return null;
        return Version.TryParse(tag.TrimStart('v', 'V'), out var v) ? v : null;
    }

    // Parses lines like "SHA256: <64-hex>  chum-1.2.zip" from the release body.
    private static string? ParseSha256(string body, string? fileName)
    {
        if (fileName is null) return null;
        foreach (var line in body.Split('\n'))
        {
            if (!line.Contains(fileName, StringComparison.OrdinalIgnoreCase)) continue;
            var hash = line.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                           .FirstOrDefault(p => p.Length == 64 && IsHex(p));
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

    // Minimal GitHub API shapes
    private sealed class GithubRelease
    {
        public string?            TagName { get; set; }
        public string?            Body    { get; set; }
        public List<GithubAsset>? Assets  { get; set; }
    }

    private sealed class GithubAsset
    {
        public string? Name               { get; set; }
        public string? BrowserDownloadUrl { get; set; }
    }
}

public record UpdateInfo(
    string  Version,
    string  ReleaseNotes,
    string? DownloadUrl,
    string? ZipFileName,
    string? Sha256);

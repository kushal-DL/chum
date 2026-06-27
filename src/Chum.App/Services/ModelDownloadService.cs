using System.IO;
using System.Net.Http;

namespace Chum.App.Services;

/// <summary>
/// Manages downloading Silero VAD ONNX model on first run.
/// Whisper model download is handled internally by Whisper.net (WhisperGgmlDownloader).
/// </summary>
public sealed class ModelDownloadService(HttpClient http)
{
    private static readonly string ModelDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Chum", "Models");

    public string SileroModelPath => Path.Combine(ModelDir, "silero_vad.onnx");
    public string WhisperModelDir => ModelDir;

    // SHA256 of silero_vad.onnx v4 from official repo
    private const string SileroUrl = "https://github.com/snakers4/silero-vad/raw/v5.0/src/silero_vad/data/silero_vad.onnx";

    public async Task EnsureSileroAsync(IProgress<string>? status = null, CancellationToken ct = default)
    {
        Directory.CreateDirectory(ModelDir);
        if (File.Exists(SileroModelPath))
        {
            status?.Report("Silero VAD model ready.");
            return;
        }

        status?.Report("Downloading Silero VAD model (~1.8 MB)...");
        Serilog.Log.Information("Downloading Silero VAD from {Url}", SileroUrl);

        using var resp = await http.GetAsync(SileroUrl, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();

        await using var fs = File.OpenWrite(SileroModelPath);
        await resp.Content.CopyToAsync(fs, ct);
        status?.Report("Silero VAD model downloaded.");
    }
}

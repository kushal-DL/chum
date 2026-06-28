using System.Net.Http;
using Chum.Audio.Models;
using Chum.Transcription.Models;
using SherpaOnnx;

namespace Chum.Transcription;

/// <summary>
/// Streaming speech-to-text using sherpa-onnx's Zipformer transducer (English, LibriSpeech).
/// Unlike Whisper (a batch model that processes fixed 30s chunks and hallucinates sound-effect
/// captions on noise), this is a true streaming ASR: it decodes faster than real-time on CPU
/// (RTF ≈ 0.1) and does not invent "(music)"/"(gunshot)" style noise text.
///
/// Model files (~130 MB) are downloaded once from the HuggingFace mirror on first run.
/// </summary>
public sealed class SherpaOnnxSttEngine : ISttEngine
{
    // HuggingFace mirror of sherpa-onnx-streaming-zipformer-en-2023-02-21.
    // Individual file access avoids the .tar.bz2 (which .NET cannot extract natively).
    private const string HfBase =
        "https://huggingface.co/csukuangfj/sherpa-onnx-streaming-zipformer-en-2023-02-21/resolve/main";

    private static readonly (string Remote, string Local)[] ModelFiles =
    [
        ("encoder-epoch-99-avg-1.int8.onnx", "encoder.int8.onnx"),
        ("decoder-epoch-99-avg-1.onnx",      "decoder.onnx"),
        ("joiner-epoch-99-avg-1.int8.onnx",  "joiner.int8.onnx"),
        ("tokens.txt",                       "tokens.txt"),
    ];

    private readonly string _modelDir;
    private readonly int _numThreads;
    private readonly object _decodeLock = new();
    private OnlineRecognizer? _recognizer;
    private bool _initialized;
    private bool _disposed;

    public bool IsReady => _initialized;
    public string AccelerationMode { get; private set; } = "CPU (Sherpa Zipformer streaming)";
    public string? DetectedLanguage => "en";

    public event EventHandler<TranscriptSegment>? SegmentTranscribed;

    public SherpaOnnxSttEngine(string modelDirectory, int numThreads = 2)
    {
        _modelDir = Path.Combine(modelDirectory, "sherpa-streaming-zipformer-en");
        _numThreads = Math.Max(1, numThreads);
    }

    public async Task InitializeAsync(IProgress<double>? downloadProgress = null, CancellationToken ct = default)
    {
        Directory.CreateDirectory(_modelDir);
        await DownloadModelsAsync(downloadProgress, ct);

        var config = new OnlineRecognizerConfig();
        config.FeatConfig.SampleRate = 16000;
        config.FeatConfig.FeatureDim = 80;
        config.ModelConfig.Transducer.Encoder = Path.Combine(_modelDir, "encoder.int8.onnx");
        config.ModelConfig.Transducer.Decoder = Path.Combine(_modelDir, "decoder.onnx");
        config.ModelConfig.Transducer.Joiner = Path.Combine(_modelDir, "joiner.int8.onnx");
        config.ModelConfig.Tokens = Path.Combine(_modelDir, "tokens.txt");
        config.ModelConfig.Provider = "cpu";
        config.ModelConfig.NumThreads = _numThreads;
        config.ModelConfig.Debug = 0;
        config.DecodingMethod = "greedy_search";
        // We feed complete clips and finish the stream ourselves, so streaming endpointing is off.
        config.EnableEndpoint = 0;

        _recognizer = new OnlineRecognizer(config);
        _initialized = true;
        Serilog.Log.Information(
            "SherpaOnnxSttEngine initialized — {Threads} threads, model dir {Dir}", _numThreads, _modelDir);
    }

    public Task<string> TranscribeAsync(float[] samples, AudioSource source, CancellationToken ct = default)
    {
        if (!_initialized || _recognizer is null)
            throw new InvalidOperationException("SherpaOnnxSttEngine not initialized.");
        return Task.Run(() => RunInference(samples, source, ct), ct);
    }

    private string RunInference(float[] samples, AudioSource source, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        string text;
        // Streams are cheap to create. Decode under a lock because the recognizer is shared between
        // the rolling-transcript loop and the press-to-record path, which can run concurrently.
        lock (_decodeLock)
        {
            var stream = _recognizer!.CreateStream();
            stream.AcceptWaveform(16000, samples);
            // Tail padding flushes the last frames out of the encoder lookahead.
            stream.AcceptWaveform(16000, new float[16000 / 2]);
            stream.InputFinished();
            while (_recognizer.IsReady(stream))
                _recognizer.Decode(stream);
            text = _recognizer.GetResult(stream).Text ?? string.Empty;
        }

        Array.Clear(samples); // privacy — zero the caller's buffer after use
        text = TranscriptCleaner.Clean(text).Trim();

        if (!string.IsNullOrWhiteSpace(text))
        {
            var seg = new TranscriptSegment(DateTimeOffset.UtcNow, source, text);
            SegmentTranscribed?.Invoke(this, seg);
        }
        return text;
    }

    private async Task DownloadModelsAsync(IProgress<double>? progress, CancellationToken ct)
    {
        // WinHttpHandler handles corporate SSL inspection (ZScaler) like the other downloaders.
        using var http = new HttpClient(new WinHttpHandler()) { Timeout = TimeSpan.FromMinutes(30) };

        for (int i = 0; i < ModelFiles.Length; i++)
        {
            var (remote, local) = ModelFiles[i];
            var localPath = Path.Combine(_modelDir, local);
            if (File.Exists(localPath))
            {
                progress?.Report((i + 1.0) / ModelFiles.Length);
                continue;
            }

            var url = $"{HfBase}/{remote}";
            Serilog.Log.Information("Downloading Sherpa model file: {File}", remote);
            progress?.Report((double)i / ModelFiles.Length);

            try
            {
                using var resp = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
                resp.EnsureSuccessStatusCode();
                var tmp = localPath + ".tmp";
                await using (var fs = File.OpenWrite(tmp))
                    await resp.Content.CopyToAsync(fs, ct);
                File.Move(tmp, localPath, overwrite: true);
                Serilog.Log.Information("Downloaded: {File}", remote);
            }
            catch (Exception ex)
            {
                var tmp = localPath + ".tmp";
                if (File.Exists(tmp)) File.Delete(tmp);
                throw new InvalidOperationException(
                    $"Failed to download Sherpa model file '{remote}': {ex.Message}", ex);
            }

            progress?.Report((i + 1.0) / ModelFiles.Length);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _recognizer?.Dispose();
    }
}

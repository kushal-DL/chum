using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using Chum.Audio.Models;
using Chum.Transcription.Models;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace Chum.Transcription;

/// <summary>
/// Whisper speech-to-text using ONNX Runtime with DirectML (Intel/AMD/NVIDIA GPU via DirectX 12).
/// Downloads encoder + decoder ONNX models from HuggingFace on first run.
/// Inference runs on the iGPU/dGPU, typically 10-20× faster than CPU.
/// </summary>
public sealed class OnnxWhisperSttEngine : ISttEngine
{
    // Whisper special token IDs (same for all model sizes)
    private const int TOKEN_EOT            = 50256;
    private const int TOKEN_SOT            = 50258;
    private const int TOKEN_LANG_EN        = 50259;
    private const int TOKEN_TRANSCRIBE     = 50359;
    private const int TOKEN_NO_TIMESTAMPS  = 50363;
    private const int TOKEN_SPECIAL_START  = 50256; // IDs ≥ this are special, skip in output

    // Xenova org on HuggingFace provides Whisper in ONNX format (same export pipeline as onnx-community)
    // Supported sizes: "small", "medium", "large-v3-turbo"
    private static string HfBase(string size) =>
        $"https://huggingface.co/Xenova/whisper-{size}/resolve/main";

    private static readonly string[] DownloadFiles =
    [
        "onnx/encoder_model.onnx",
        "onnx/decoder_model.onnx",
        "tokenizer.json",
    ];

    private readonly string _modelDir;
    private readonly string _hfBase;
    private InferenceSession? _encoder;
    private InferenceSession? _decoder;
    private string[]? _vocab;   // token_id → token_string (may have gaps as null)
    private int _encoderOutDim; // 384=tiny, 512=base/small, 1024=medium/large

    private bool _initialized;
    private bool _disposed;

    public bool IsReady => _initialized;
    public string AccelerationMode { get; private set; } = "GPU (DirectML)";
    public string? DetectedLanguage { get; private set; } = "en";

    public event EventHandler<TranscriptSegment>? SegmentTranscribed;

    public OnnxWhisperSttEngine(string modelDirectory, string modelSize = "medium")
    {
        _modelDir = Path.Combine(modelDirectory, $"whisper-{modelSize}-onnx");
        _hfBase = HfBase(modelSize);
    }

    /// <summary>Downloads models (once) and creates DirectML ONNX sessions.</summary>
    public async Task InitializeAsync(IProgress<double>? downloadProgress = null, CancellationToken ct = default)
    {
        Directory.CreateDirectory(_modelDir);

        await DownloadModelsAsync(downloadProgress, ct);

        var encoderPath = Path.Combine(_modelDir, "encoder_model.onnx");
        var decoderPath = Path.Combine(_modelDir, "decoder_model.onnx");
        var tokenizerPath = Path.Combine(_modelDir, "tokenizer.json");

        // Attempt DirectML session, gracefully fall back to CPU ONNX if GPU not available
        SessionOptions BuildOpts(bool useDml)
        {
            var o = new SessionOptions();
            o.GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL;
            o.ExecutionMode = ExecutionMode.ORT_SEQUENTIAL;
            if (useDml) o.AppendExecutionProvider_DML(0); // device 0 = first GPU/iGPU
            return o;
        }

        bool triedDml = false;
        try
        {
            using var dmlOpts = BuildOpts(useDml: true);
            _encoder = new InferenceSession(encoderPath, dmlOpts);
            _decoder = new InferenceSession(decoderPath, dmlOpts);
            triedDml = true;
            AccelerationMode = "GPU (DirectML)";
            Serilog.Log.Information("OnnxWhisperSttEngine: running on DirectML (iGPU/dGPU)");
        }
        catch (Exception ex) when (!triedDml || ex is OnnxRuntimeException)
        {
            // DirectML provider registration failed OR session creation failed — retry on CPU
            Serilog.Log.Warning(ex, "DirectML session failed — falling back to CPU ONNX inference");
            _encoder?.Dispose();
            _decoder?.Dispose();
            using var cpuOpts = BuildOpts(useDml: false);
            _encoder = new InferenceSession(encoderPath, cpuOpts);
            _decoder = new InferenceSession(decoderPath, cpuOpts);
            AccelerationMode = "CPU (ONNX)";
        }

        // Infer encoder output dimension from model metadata
        var encOut = _encoder.OutputMetadata["last_hidden_state"];
        _encoderOutDim = encOut.Dimensions[2]; // (batch, 1500, dim)

        _vocab = LoadVocab(tokenizerPath);
        _initialized = true;

        Serilog.Log.Information(
            "OnnxWhisperSttEngine initialized — encoder_dim={Dim} acc={Mode}",
            _encoderOutDim, AccelerationMode);
    }

    /// <summary>Transcribes float PCM (16 kHz, mono). Fires SegmentTranscribed on success.</summary>
    public Task<string> TranscribeAsync(float[] samples, AudioSource source, CancellationToken ct = default)
    {
        if (!_initialized || _encoder is null || _decoder is null || _vocab is null)
            throw new InvalidOperationException("OnnxWhisperSttEngine not initialized.");

        // Run on thread pool — ONNX inference is CPU/GPU bound, not I/O bound
        return Task.Run(() => RunInference(samples, source, ct), ct);
    }

    private string RunInference(float[] samples, AudioSource source, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        // ── Mel spectrogram ──────────────────────────────────────────────────
        float[] mel = MelSpectrogram.Compute(samples);

        // Shape [1, 80, 3000]
        var melTensor = new DenseTensor<float>(mel,
            new[] { 1, MelSpectrogram.N_MELS, MelSpectrogram.N_FRAMES });

        ct.ThrowIfCancellationRequested();

        // ── Encoder ──────────────────────────────────────────────────────────
        var encoderInputs = new NamedOnnxValue[]
        {
            NamedOnnxValue.CreateFromTensor("input_features", melTensor)
        };

        float[] encoderOut;
        using (var encoderResult = _encoder!.Run(encoderInputs))
        {
            var hiddenState = encoderResult.First(o => o.Name == "last_hidden_state");
            encoderOut = hiddenState.AsEnumerable<float>().ToArray();
        }

        ct.ThrowIfCancellationRequested();

        // ── Greedy decoder ───────────────────────────────────────────────────
        // Initial prompt: <|startoftranscript|> <|en|> <|transcribe|> <|notimestamps|>
        var inputIds = new List<long> { TOKEN_SOT, TOKEN_LANG_EN, TOKEN_TRANSCRIBE, TOKEN_NO_TIMESTAMPS };
        const int MAX_TOKENS = 448;

        var encoderTensor = new DenseTensor<float>(encoderOut,
            new[] { 1, 1500, _encoderOutDim });

        while (inputIds.Count < MAX_TOKENS)
        {
            ct.ThrowIfCancellationRequested();

            var idArray = inputIds.ToArray();
            var idTensor = new DenseTensor<long>(idArray, new[] { 1, idArray.Length });

            var decoderInputs = new NamedOnnxValue[]
            {
                NamedOnnxValue.CreateFromTensor("input_ids", idTensor),
                NamedOnnxValue.CreateFromTensor("encoder_hidden_states", encoderTensor)
            };

            float[] logits;
            using (var decoderResult = _decoder!.Run(decoderInputs))
            {
                var logitOutput = decoderResult.First(o => o.Name == "logits");
                logits = logitOutput.AsEnumerable<float>().ToArray();
            }

            // logits shape: (1, seq_len, vocab_size) — take last position
            int vocabSize = logits.Length / inputIds.Count;
            int lastPos = (inputIds.Count - 1) * vocabSize;
            int nextToken = Argmax(logits, lastPos, vocabSize);

            if (nextToken == TOKEN_EOT)
                break;

            inputIds.Add(nextToken);
        }

        // ── Decode tokens to text ─────────────────────────────────────────────
        // Skip the 4 initial prompt tokens; skip all special tokens (id ≥ 50256)
        string text = DecodeTokens(inputIds.Skip(4), _vocab!);

        // Zero audio buffer (privacy)
        Array.Clear(samples);

        text = TranscriptCleaner.Clean(text);

        if (!string.IsNullOrWhiteSpace(text) && !IsHallucination(text))
        {
            var seg = new TranscriptSegment(DateTimeOffset.UtcNow, source, text);
            SegmentTranscribed?.Invoke(this, seg);
            return text;
        }

        return string.Empty;
    }

    private static int Argmax(float[] logits, int start, int length)
    {
        int best = 0;
        float bestVal = float.NegativeInfinity;
        for (int i = 0; i < length; i++)
        {
            if (logits[start + i] > bestVal)
            {
                bestVal = logits[start + i];
                best = i;
            }
        }
        return best;
    }

    private static string DecodeTokens(IEnumerable<long> tokenIds, string[] vocab)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var id in tokenIds)
        {
            if (id >= TOKEN_SPECIAL_START) continue;
            if (id < 0 || id >= vocab.Length) continue;
            var tok = vocab[id];
            if (tok is null) continue;

            foreach (char c in tok)
            {
                // GPT-2 byte-level BPE: Ġ (U+0120) = space, Ċ (U+010A) = newline
                if (c == 'Ġ') sb.Append(' ');
                else if (c == 'Ċ') sb.Append('\n');
                else sb.Append(c);
            }
        }
        return sb.ToString().Trim();
    }

    // ── Vocabulary loading ────────────────────────────────────────────────────

    private static string[] LoadVocab(string tokenizerPath)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(tokenizerPath));
        var root = doc.RootElement;

        // tokenizer.json → model.vocab = { "token": id, ... }
        var vocabObj = root.GetProperty("model").GetProperty("vocab");

        int maxId = 0;
        foreach (var entry in vocabObj.EnumerateObject())
            if (entry.Value.GetInt32() > maxId) maxId = entry.Value.GetInt32();

        var vocab = new string[maxId + 1];
        foreach (var entry in vocabObj.EnumerateObject())
            vocab[entry.Value.GetInt32()] = entry.Name;

        // Merge added_tokens (special tokens with string representations)
        if (root.TryGetProperty("added_tokens", out var addedTokens))
        {
            foreach (var token in addedTokens.EnumerateArray())
            {
                int id = token.GetProperty("id").GetInt32();
                string content = token.GetProperty("content").GetString() ?? "";
                if (id < vocab.Length) vocab[id] = content;
            }
        }

        Serilog.Log.Information("OnnxWhisperSttEngine: loaded {Count} vocabulary entries", vocab.Length);
        return vocab;
    }

    // ── Model download ────────────────────────────────────────────────────────

    private async Task DownloadModelsAsync(IProgress<double>? progress, CancellationToken ct)
    {
        // Use WinHttpHandler to handle corporate SSL inspection (ZScaler) like the LLM providers
        using var http = new HttpClient(new WinHttpHandler()) { Timeout = TimeSpan.FromMinutes(30) };

        for (int i = 0; i < DownloadFiles.Length; i++)
        {
            var relPath = DownloadFiles[i];
            // Flatten the onnx/ subdirectory to the model dir for simplicity
            var localName = Path.GetFileName(relPath);
            var localPath = Path.Combine(_modelDir, localName);

            if (File.Exists(localPath))
            {
                progress?.Report((i + 1.0) / DownloadFiles.Length);
                continue;
            }

            var url = $"{_hfBase}/{relPath}";
            Serilog.Log.Information("Downloading ONNX Whisper model file: {File}", relPath);
            progress?.Report((double)i / DownloadFiles.Length);

            try
            {
                using var resp = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
                resp.EnsureSuccessStatusCode();

                var tmpPath = localPath + ".tmp";
                await using (var fs = File.OpenWrite(tmpPath))
                    await resp.Content.CopyToAsync(fs, ct);

                File.Move(tmpPath, localPath, overwrite: true);
                Serilog.Log.Information("Downloaded: {File} → {Path}", relPath, localPath);
            }
            catch (Exception ex)
            {
                // Clean up partial file if download failed
                var tmpPath = localPath + ".tmp";
                if (File.Exists(tmpPath)) File.Delete(tmpPath);
                throw new InvalidOperationException(
                    $"Failed to download ONNX Whisper model file '{relPath}': {ex.Message}", ex);
            }

            progress?.Report((i + 1.0) / DownloadFiles.Length);
        }
    }

    // ── Hallucination filter (mirrors WhisperSttEngine) ───────────────────────

    private static readonly HashSet<string> _hallucinations =
    [
        "[BLANK_AUDIO]", "[ Silence ]", "[silence]", "[SILENCE]",
        "(MUSIC)", "[MUSIC]", "(APPLAUSE)", "[APPLAUSE]",
        "[INAUDIBLE]", "(INAUDIBLE)", "[NOISE]", "(NOISE)",
        "Thanks for watching!", "Thank you.", "Thank you!",
        "Thank you for watching.", "Please subscribe.",
        "Subtitles by the Amara.org community",
    ];

    private static bool IsHallucination(string text)
    {
        foreach (var h in _hallucinations)
            if (text.Contains(h, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _encoder?.Dispose();
        _decoder?.Dispose();
    }
}

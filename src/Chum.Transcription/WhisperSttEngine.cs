using System.Text;
using Chum.Audio.Models;
using Chum.Transcription.Models;
using Whisper.net;
using Whisper.net.Ggml;

namespace Chum.Transcription;

/// <summary>
/// Runs Whisper.net (whisper.cpp) locally for speech-to-text.
/// Model is downloaded from Hugging Face on first use (~244 MB for 'small').
/// Transcription runs on a dedicated background thread; never blocks the UI.
/// </summary>
public sealed class WhisperSttEngine : IDisposable
{
    private WhisperFactory? _factory;
    private WhisperProcessor? _processor;
    private readonly string _modelPath;
    private readonly GgmlType _modelType;
    private bool _initialized;

    public event EventHandler<TranscriptSegment>? SegmentTranscribed;

    public bool IsReady => _initialized;

    public WhisperSttEngine(string modelDirectory, GgmlType modelType = GgmlType.Small)
    {
        _modelType = modelType;
        _modelPath = Path.Combine(modelDirectory, $"ggml-{modelType.ToString().ToLowerInvariant()}.bin");
    }

    /// <summary>Downloads model if needed, then loads it. Call once at startup on a background thread.</summary>
    public async Task InitializeAsync(IProgress<double>? downloadProgress = null, CancellationToken ct = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_modelPath)!);

        if (!File.Exists(_modelPath))
        {
            Serilog.Log.Information("Downloading Whisper {Model} model to {Path}...", _modelType, _modelPath);
            using var modelStream = await WhisperGgmlDownloader.GetGgmlModelAsync(_modelType, cancellationToken: ct);
            using var fileWriter = File.OpenWrite(_modelPath);
            await modelStream.CopyToAsync(fileWriter, ct);
            Serilog.Log.Information("Whisper model download complete.");
        }

        _factory = WhisperFactory.FromPath(_modelPath);
        _processor = _factory.CreateBuilder()
            .WithLanguage("auto")
            .Build();

        _initialized = true;
        Serilog.Log.Information("Whisper {Model} loaded from {Path}", _modelType, _modelPath);
    }

    /// <summary>
    /// Transcribes a float[] PCM segment (16 kHz, mono).
    /// Converts to in-memory WAV before passing to Whisper.net.
    /// </summary>
    public async Task<string> TranscribeAsync(float[] samples, AudioSource source, CancellationToken ct = default)
    {
        if (!_initialized || _processor is null)
            throw new InvalidOperationException("WhisperSttEngine not initialized. Call InitializeAsync first.");

        using var wavStream = BuildWavStream(samples);
        var sb = new StringBuilder();

        await foreach (var segment in _processor.ProcessAsync(wavStream, ct))
        {
            var text = segment.Text?.Trim() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(text) && !IsHallucination(text))
                sb.Append(text).Append(' ');
        }

        var result = TranscriptCleaner.Clean(sb.ToString());

        // Zero raw samples after transcription (privacy: don't keep audio in memory)
        Array.Clear(samples);

        if (!string.IsNullOrWhiteSpace(result))
        {
            var transcriptSegment = new TranscriptSegment(DateTimeOffset.UtcNow, source, result);
            SegmentTranscribed?.Invoke(this, transcriptSegment);
        }

        return result;
    }

    private static readonly HashSet<string> _hallucinations =
    [
        // Whisper segment-level silence/noise markers
        "[BLANK_AUDIO]", "[ Silence ]", "[silence]", "[SILENCE]",
        "(MUSIC)", "[MUSIC]", "(APPLAUSE)", "[APPLAUSE]",
        "[INAUDIBLE]", "(INAUDIBLE)", "[NOISE]", "(NOISE)",
        "[SOUND]", "(SOUND)", "[LAUGHTER]", "(LAUGHTER)",
        // Common hallucinations on near-silence audio
        "Thanks for watching!", "Thank you.", "Thank you!",
        "Thank you for watching.", "Please subscribe.",
        "Subtitles by the Amara.org community",
        "This video is brought to you by",
    ];

    private static bool IsHallucination(string text)
    {
        foreach (var h in _hallucinations)
            if (text.Contains(h, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    /// <summary>Wraps float[] PCM into a minimal WAV MemoryStream for Whisper.net.</summary>
    private static MemoryStream BuildWavStream(float[] samples)
    {
        const int sampleRate = 16_000;
        const short channels = 1;
        const short bitsPerSample = 16;
        int dataBytes = samples.Length * 2;

        var ms = new MemoryStream(44 + dataBytes);
        using var bw = new BinaryWriter(ms, Encoding.ASCII, leaveOpen: true);

        bw.Write("RIFF"u8.ToArray());
        bw.Write(36 + dataBytes);
        bw.Write("WAVE"u8.ToArray());
        bw.Write("fmt "u8.ToArray());
        bw.Write(16);                  // chunk size
        bw.Write((short)1);            // PCM
        bw.Write(channels);
        bw.Write(sampleRate);
        bw.Write(sampleRate * channels * bitsPerSample / 8);
        bw.Write((short)(channels * bitsPerSample / 8));
        bw.Write(bitsPerSample);
        bw.Write("data"u8.ToArray());
        bw.Write(dataBytes);

        foreach (var s in samples)
            bw.Write((short)Math.Clamp(s * 32767f, -32768f, 32767f));

        ms.Position = 0;
        return ms;
    }

    public void Dispose()
    {
        _processor?.Dispose();
        _factory?.Dispose();
    }
}

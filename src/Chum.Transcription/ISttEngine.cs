using Chum.Audio.Models;
using Chum.Transcription.Models;

namespace Chum.Transcription;

/// <summary>
/// Common contract for local and GPU-accelerated speech-to-text engines.
/// </summary>
public interface ISttEngine : IDisposable
{
    bool IsReady { get; }

    /// <summary>Human-readable acceleration mode shown in the overlay (e.g. "CPU", "GPU (DirectML)").</summary>
    string AccelerationMode { get; }

    /// <summary>ISO language code detected by the engine, or null until first segment.</summary>
    string? DetectedLanguage { get; }

    event EventHandler<TranscriptSegment>? SegmentTranscribed;

    Task InitializeAsync(IProgress<double>? downloadProgress = null, CancellationToken ct = default);

    Task<string> TranscribeAsync(float[] samples, AudioSource source, CancellationToken ct = default);
}

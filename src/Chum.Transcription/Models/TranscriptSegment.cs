using Chum.Audio.Models;

namespace Chum.Transcription.Models;

public sealed record TranscriptSegment(
    DateTimeOffset Timestamp,
    AudioSource Source,
    string Text,
    float Confidence = 1.0f
)
{
    /// Speaker label shown in transcript and LLM context.
    public string SpeakerLabel => Source == AudioSource.Microphone ? "Me" : "Remote";
}

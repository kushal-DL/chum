namespace Chum.Audio.Models;

public enum AudioSource { Loopback, Microphone }

/// <summary>A single VAD-gated speech segment ready for transcription.</summary>
public sealed record AudioChunk(
    float[] Samples,       // 16 kHz, mono, float32
    AudioSource Source,
    DateTimeOffset Timestamp
);

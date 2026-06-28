namespace Chum.Llm;

public sealed record LlmRequest(
    string SystemPrompt,
    string UserMessage,
    string? ImageBase64 = null,    // JPEG base64 for vision queries
    string? ImageMediaType = null, // e.g. "image/jpeg"
    string? AudioBase64 = null,    // WAV base64 for models that accept audio input (e.g. NVIDIA NIM, GPT-4o)
    string? AudioMediaType = null, // e.g. "audio/wav"
    int MaxTokens = 1024,
    float Temperature = 0.3f
);

public interface ILlmProvider
{
    string ProviderName { get; }
    string ModelId { get; }

    /// <summary>
    /// True only for models that accept raw audio via the input_audio content block
    /// (currently: OpenAI gpt-4o-audio-preview). All other providers/models return false
    /// and the orchestrator will fall back to local transcription → text → LLM.
    /// </summary>
    bool SupportsAudioInput { get; }

    /// <summary>Fired after each stream completes with actual token counts and estimated cost.</summary>
    event EventHandler<LlmUsage>? UsageRecorded;

    /// <summary>Streams response tokens. Each yielded string is one or more new characters.</summary>
    IAsyncEnumerable<string> StreamResponseAsync(LlmRequest request, CancellationToken ct = default);
}

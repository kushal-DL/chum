using System.Net.Http.Headers;
using System.Text.Json;
using Chum.Audio.Models;
using Chum.Transcription.Models;

namespace Chum.Transcription;

/// <summary>
/// Cloud STT using an OpenAI-compatible /v1/audio/transcriptions endpoint.
/// Works with OpenAI (whisper-1) or NVIDIA NIM (nvidia/canary-1b, nvidia/parakeet-ctc-1.1b-asr).
/// Configure <paramref name="baseUrl"/> to switch provider — leave null for OpenAI.
/// </summary>
public sealed class OpenAiSttProvider : IDisposable
{
    private readonly HttpClient _http;
    private readonly string _model;
    private readonly string _transcriptionUrl;

    public event EventHandler<TranscriptSegment>? SegmentTranscribed;

    public OpenAiSttProvider(string apiKey, string model = "whisper-1", string? baseUrl = null)
    {
        _model = model;
        var apiBase = string.IsNullOrWhiteSpace(baseUrl)
            ? "https://api.openai.com/v1"
            : baseUrl.TrimEnd('/');
        _transcriptionUrl = apiBase + "/audio/transcriptions";

        _http = new HttpClient();
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
    }

    /// <summary>
    /// Transcribes float PCM (16 kHz, mono) via the cloud Whisper-compatible API.
    /// Clears the sample buffer after transcription (privacy: don't keep audio in memory).
    /// </summary>
    public async Task<string> TranscribeAsync(float[] samples, AudioSource source, CancellationToken ct = default)
    {
        var wavBytes = WavEncoder.ToWavBytes(samples);
        using var wavStream = new MemoryStream(wavBytes);

        using var content = new MultipartFormDataContent();
        var fileContent = new StreamContent(wavStream);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
        content.Add(fileContent, "file", "audio.wav");
        content.Add(new StringContent(_model), "model");

        using var response = await _http.PostAsync(_transcriptionUrl, content, ct);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            Array.Clear(samples);
            throw new SttException($"Cloud STT API returned {(int)response.StatusCode}: {body}");
        }

        var json = await response.Content.ReadAsStringAsync(ct);
        var text = JsonDocument.Parse(json).RootElement
            .GetProperty("text").GetString()?.Trim() ?? string.Empty;

        Array.Clear(samples);

        text = TranscriptCleaner.Clean(text);

        if (!string.IsNullOrWhiteSpace(text))
        {
            var segment = new TranscriptSegment(DateTimeOffset.UtcNow, source, text);
            SegmentTranscribed?.Invoke(this, segment);
        }

        return text;
    }

    public void Dispose() => _http.Dispose();
}

public sealed class SttException(string message) : Exception(message);

using System.Net.Http.Headers;
using System.Text.Json;
using Chum.Audio.Models;
using Chum.Transcription.Models;

namespace Chum.Transcription;

/// <summary>
/// Cloud STT fallback using OpenAI Whisper API (POST /v1/audio/transcriptions).
/// Used when local Whisper is not ready or fails. Shares the same float PCM interface.
/// </summary>
public sealed class OpenAiSttProvider : IDisposable
{
    private readonly HttpClient _http;
    private readonly string _model;

    public event EventHandler<TranscriptSegment>? SegmentTranscribed;

    public OpenAiSttProvider(string apiKey, string model = "whisper-1")
    {
        _model = model;
        _http = new HttpClient();
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
    }

    /// <summary>
    /// Transcribes float PCM (16 kHz, mono) via the OpenAI Whisper API.
    /// Clears the sample buffer after transcription (privacy: don't keep audio in memory).
    /// </summary>
    public async Task<string> TranscribeAsync(float[] samples, AudioSource source, CancellationToken ct = default)
    {
        using var wavStream = WhisperSttEngine.BuildWavStream(samples);

        using var content = new MultipartFormDataContent();
        var fileContent = new StreamContent(wavStream);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
        content.Add(fileContent, "file", "audio.wav");
        content.Add(new StringContent(_model), "model");

        using var response = await _http.PostAsync(
            "https://api.openai.com/v1/audio/transcriptions", content, ct);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            Array.Clear(samples);
            throw new SttException($"OpenAI Whisper API returned {(int)response.StatusCode}: {body}");
        }

        var json = await response.Content.ReadAsStringAsync(ct);
        var text = JsonDocument.Parse(json).RootElement
            .GetProperty("text").GetString()?.Trim() ?? string.Empty;

        // Zero raw samples after transcription (privacy)
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

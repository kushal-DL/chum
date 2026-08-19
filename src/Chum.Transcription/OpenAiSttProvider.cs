using System.Net.Http.Headers;
using System.Text.Json;
using Chum.Audio.Models;
using Chum.Transcription.Models;

namespace Chum.Transcription;

/// <summary>
/// Primary STT engine backed by a local Whisper server at http://localhost:8000 (or any
/// OpenAI-compatible /v1/audio/transcriptions endpoint).  Implements ISttEngine so it
/// drives the rolling transcript and press-to-record queries without sherpa-onnx.
/// </summary>
public sealed class OpenAiSttProvider : ISttEngine
{
    private readonly HttpClient _http;
    private readonly string _model;
    private readonly string _transcriptionUrl;

    public bool IsReady => true;
    public string AccelerationMode => "Whisper API (local)";
    public string? DetectedLanguage => null;

    public event EventHandler<TranscriptSegment>? SegmentTranscribed;

    public OpenAiSttProvider(string apiKey = "", string model = "whisper-large-v3-turbo", string? baseUrl = null)
    {
        _model = model;
        var apiBase = string.IsNullOrWhiteSpace(baseUrl)
            ? "https://api.openai.com/v1"
            : baseUrl.TrimEnd('/');
        _transcriptionUrl = apiBase + "/audio/transcriptions";

        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(120) };
        if (!string.IsNullOrWhiteSpace(apiKey))
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
    }

    public Task InitializeAsync(IProgress<double>? downloadProgress = null, CancellationToken ct = default)
        => Task.CompletedTask;

    // whisper.cpp returns nothing for audio shorter than ~1s ("input is too short — consider
    // padding the input audio with silence"). The rolling transcript's short VAD segments hit
    // that floor and were silently dropped, so pad them with trailing silence to a safe minimum.
    private const int MinTranscribeSamples = 16_000 * 6 / 5; // 1.2s at 16 kHz

    /// <summary>
    /// Transcribes float PCM (16 kHz, mono) via the cloud Whisper-compatible API.
    /// Clears the sample buffer after transcription (privacy: don't keep audio in memory).
    /// </summary>
    public async Task<string> TranscribeAsync(float[] samples, AudioSource source, CancellationToken ct = default)
    {
        // Pad clips below whisper.cpp's 1s floor so they aren't dropped (see MinTranscribeSamples).
        float[] toSend = samples;
        if (samples.Length is > 0 and < MinTranscribeSamples)
        {
            toSend = new float[MinTranscribeSamples];
            Array.Copy(samples, toSend, samples.Length);
        }

        var wavBytes = WavEncoder.ToWavBytes(toSend);
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

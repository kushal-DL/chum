using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Chum.Llm;

/// <summary>
/// Streams responses from a locally-running Ollama instance (http://localhost:11434).
/// Uses Ollama's /api/chat endpoint with NDJSON streaming.
/// Vision: pass base64 image in the "images" array — only works with multimodal models (llava, llava-llama3, etc.).
/// </summary>
public sealed class OllamaLlmProvider : ILlmProvider
{
    private readonly string _baseUrl;
    private readonly HttpClient _http;

    public string ProviderName => "Ollama (local)";
    public string ModelId { get; }
    public bool SupportsAudioInput => false;

    // Local inference has no API cost — UsageRecorded is never fired for Ollama.
    public event EventHandler<LlmUsage>? UsageRecorded;

    public OllamaLlmProvider(string model, string baseUrl = "http://localhost:11434")
    {
        ModelId = model;
        _baseUrl = baseUrl.TrimEnd('/');
        _http = new HttpClient(new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(10) })
            { Timeout = TimeSpan.FromSeconds(120) };
    }

    public async IAsyncEnumerable<string> StreamResponseAsync(
        LlmRequest request,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var body = BuildRequestBody(request);
        var json = JsonSerializer.Serialize(body);

        HttpResponseMessage response;
        try
        {
            var req = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/api/chat")
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
            response = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        }
        catch (HttpRequestException ex)
        {
            throw new LlmException(
                "Cannot reach Ollama. Make sure Ollama is running (`ollama serve`) before using local mode.", ex);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new LlmException("Ollama request timed out — model may be too large for this machine.", ex);
        }

        if (!response.IsSuccessStatusCode)
        {
            var err = await response.Content.ReadAsStringAsync(ct);
            throw new LlmException($"Ollama error {(int)response.StatusCode}: {err}");
        }

        using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);

        // Ollama streams NDJSON: one JSON object per line, done:true on the last line
        while (!ct.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(ct);
            if (line is null) break; // null = clean EOF
            if (string.IsNullOrWhiteSpace(line)) continue;

            string? token = null;
            bool done = false;
            try
            {
                var node = JsonNode.Parse(line);
                done = node?["done"]?.GetValue<bool>() ?? false;
                token = node?["message"]?["content"]?.GetValue<string>();
            }
            catch (JsonException)
            {
                continue; // malformed line — skip
            }

            if (token is not null && token.Length > 0)
                yield return token;

            if (done) break;
        }
    }

    private object BuildRequestBody(LlmRequest request)
    {
        // System message as a separate entry so the model receives clear role separation
        var messages = new List<object>
        {
            new { role = "system", content = request.SystemPrompt }
        };

        if (request.ImagesBase64 is { Count: > 0 })
        {
            // Multi-image batch (capture session)
            messages.Add(new
            {
                role = "user",
                content = request.UserMessage,
                images = request.ImagesBase64.ToArray()
            });
        }
        else if (request.ImageBase64 is not null)
        {
            // Ollama vision format: images array alongside content text
            messages.Add(new
            {
                role = "user",
                content = request.UserMessage,
                images = new[] { request.ImageBase64 }
            });
        }
        else
        {
            messages.Add(new { role = "user", content = request.UserMessage });
        }

        return new
        {
            model = ModelId,
            messages,
            stream = true,
            options = new
            {
                temperature = (double)request.Temperature,
                num_predict = request.MaxTokens
            }
        };
    }
}

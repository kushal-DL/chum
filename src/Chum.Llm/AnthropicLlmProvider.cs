using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Chum.Llm;

/// <summary>
/// Calls the Anthropic Messages API via raw HttpClient + SSE parsing.
/// No third-party SDK required — avoids version fragility.
///
/// Supported models: claude-haiku-4-5-20251001 (default, fast), claude-sonnet-4-6 (quality/vision)
/// </summary>
public sealed class AnthropicLlmProvider : ILlmProvider
{
    private const string ApiBase = "https://api.anthropic.com/v1/messages";
    private const string AnthropicVersion = "2023-06-01";

    private readonly HttpClient _http;
    private readonly string _apiKey;

    public string ProviderName => "Anthropic";
    public string ModelId { get; }
    public bool SupportsAudioInput => false;

    public event EventHandler<LlmUsage>? UsageRecorded;

    public AnthropicLlmProvider(string apiKey, string modelId = "claude-haiku-4-5-20251001")
    {
        _apiKey = apiKey;
        ModelId = modelId;
        _http = new HttpClient(new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(10) })
            { Timeout = TimeSpan.FromSeconds(120) };
    }

    public async IAsyncEnumerable<string> StreamResponseAsync(
        LlmRequest request,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var body = BuildRequestBody(request);
        using var req = new HttpRequestMessage(HttpMethod.Post, ApiBase);
        req.Headers.Add("x-api-key", _apiKey);
        req.Headers.Add("anthropic-version", AnthropicVersion);
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        req.Content = new StringContent(body, Encoding.UTF8, "application/json");

        HttpResponseMessage? response = null;
        try
        {
            response = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        }
        catch (HttpRequestException ex)
        {
            throw new LlmException($"Network error calling Anthropic API: {ex.Message}", ex);
        }

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct);
            throw new LlmException($"Anthropic API error {(int)response.StatusCode}: {errorBody}");
        }

        using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);

        int inputTokens = 0;
        int outputTokens = 0;

        // Parse SSE: lines starting with "data: " contain JSON event objects
        while (!ct.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(ct);
            if (line is null) break; // null = clean EOF
            if (!line.StartsWith("data: ", StringComparison.Ordinal)) continue;

            var json = line["data: ".Length..];
            if (json == "[DONE]") break;

            string? delta = null;
            try
            {
                var node = JsonNode.Parse(json);
                var type = node?["type"]?.GetValue<string>();

                if (type == "message_start")
                    inputTokens = node?["message"]?["usage"]?["input_tokens"]?.GetValue<int>() ?? 0;
                else if (type == "content_block_delta")
                    delta = node?["delta"]?["text"]?.GetValue<string>();
                else if (type == "message_delta")
                    outputTokens = node?["usage"]?["output_tokens"]?.GetValue<int>() ?? outputTokens;
                else if (type == "error")
                    throw new LlmException($"Anthropic stream error: {node?["error"]?["message"]?.GetValue<string>()}");
            }
            catch (JsonException)
            {
                continue; // malformed line — skip
            }

            if (delta is not null)
                yield return delta;
        }

        if (inputTokens + outputTokens > 0)
        {
            var cost = LlmPricing.EstimateCost(ModelId, inputTokens, outputTokens);
            UsageRecorded?.Invoke(this, new LlmUsage(ModelId, inputTokens, outputTokens, cost));
        }
    }

    private string BuildRequestBody(LlmRequest request)
    {
        var contentArray = new JsonArray();

        // Single image (snapshot / snip)
        if (request.ImageBase64 is not null)
        {
            contentArray.Add(new JsonObject
            {
                ["type"] = "image",
                ["source"] = new JsonObject
                {
                    ["type"] = "base64",
                    ["media_type"] = request.ImageMediaType ?? "image/jpeg",
                    ["data"] = request.ImageBase64
                }
            });
        }

        // Multi-image batch (capture session) — images precede the text prompt
        if (request.ImagesBase64 is { Count: > 0 })
        {
            foreach (var img in request.ImagesBase64)
            {
                contentArray.Add(new JsonObject
                {
                    ["type"] = "image",
                    ["source"] = new JsonObject
                    {
                        ["type"] = "base64",
                        ["media_type"] = "image/jpeg",
                        ["data"] = img
                    }
                });
            }
        }

        contentArray.Add(new JsonObject
        {
            ["type"] = "text",
            ["text"] = request.UserMessage
        });

        var body = new JsonObject
        {
            ["model"] = ModelId,
            ["max_tokens"] = request.MaxTokens,
            ["temperature"] = request.Temperature,
            ["stream"] = true,
            ["system"] = request.SystemPrompt,
            ["messages"] = new JsonArray
            {
                new JsonObject { ["role"] = "user", ["content"] = contentArray }
            }
        };

        return body.ToJsonString();
    }
}

public sealed class LlmException(string message, Exception? inner = null) : Exception(message, inner);

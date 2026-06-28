using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Chum.Llm;

/// <summary>
/// Calls the OpenAI Chat Completions API via raw HttpClient + SSE parsing.
/// Supports gpt-4o-mini (fast/cheap) and gpt-4o (quality/vision).
/// </summary>
public sealed class OpenAiLlmProvider : ILlmProvider
{
    private readonly string _apiBase;

    private readonly HttpClient _http;
    private readonly string _apiKey;

    public string ProviderName => "OpenAI";
    public string ModelId { get; }

    public event EventHandler<LlmUsage>? UsageRecorded;

    public OpenAiLlmProvider(string apiKey, string modelId = "gpt-4o-mini", string? baseUrl = null)
    {
        _apiKey = apiKey;
        ModelId = modelId;
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(120) };
        _apiBase = string.IsNullOrWhiteSpace(baseUrl)
            ? "https://api.openai.com/v1/chat/completions"
            : baseUrl.TrimEnd('/') + "/chat/completions";
    }

    public async IAsyncEnumerable<string> StreamResponseAsync(
        LlmRequest request,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var body = BuildRequestBody(request);
        using var req = new HttpRequestMessage(HttpMethod.Post, _apiBase);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        req.Content = new StringContent(body, Encoding.UTF8, "application/json");

        HttpResponseMessage? response = null;
        try
        {
            response = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        }
        catch (HttpRequestException ex)
        {
            throw new LlmException($"Network error calling OpenAI API: {ex.Message}", ex);
        }

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct);
            throw new LlmException($"OpenAI API error {(int)response.StatusCode}: {errorBody}");
        }

        using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);

        int inputTokens = 0;
        int outputTokens = 0;

        while (!reader.EndOfStream && !ct.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(ct);
            if (line is null) break;
            if (!line.StartsWith("data: ", StringComparison.Ordinal)) continue;

            var json = line["data: ".Length..];
            if (json == "[DONE]") break;

            string? delta = null;
            try
            {
                var node = JsonNode.Parse(json);
                delta = node?["choices"]?[0]?["delta"]?["content"]?.GetValue<string>();
                // Usage arrives in the final chunk when stream_options.include_usage = true
                if (node?["usage"] is { } usage)
                {
                    inputTokens = usage["prompt_tokens"]?.GetValue<int>() ?? inputTokens;
                    outputTokens = usage["completion_tokens"]?.GetValue<int>() ?? outputTokens;
                }
            }
            catch (JsonException)
            {
                continue; // malformed SSE line — skip
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
        JsonNode userContent;
        if (request.ImageBase64 is not null)
        {
            var mediaType = request.ImageMediaType ?? "image/jpeg";
            userContent = new JsonArray
            {
                new JsonObject
                {
                    ["type"] = "image_url",
                    ["image_url"] = new JsonObject
                    {
                        ["url"] = $"data:{mediaType};base64,{request.ImageBase64}"
                    }
                },
                new JsonObject { ["type"] = "text", ["text"] = request.UserMessage }
            };
        }
        else
        {
            userContent = JsonValue.Create(request.UserMessage)!;
        }

        var body = new JsonObject
        {
            ["model"] = ModelId,
            ["max_tokens"] = request.MaxTokens,
            ["temperature"] = (double)request.Temperature,
            ["stream"] = true,
            ["stream_options"] = new JsonObject { ["include_usage"] = true },
            ["messages"] = new JsonArray
            {
                new JsonObject { ["role"] = "system", ["content"] = request.SystemPrompt },
                new JsonObject { ["role"] = "user", ["content"] = userContent }
            }
        };

        return body.ToJsonString();
    }
}

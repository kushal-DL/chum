using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using Serilog;

namespace Chum.App.Services;

public sealed class RagBridgeService
{
    private static readonly HttpClient _http = new(new SocketsHttpHandler
    {
        ConnectTimeout = TimeSpan.FromSeconds(5),
    })
    {
        Timeout = TimeSpan.FromSeconds(10),
    };

    private readonly SettingsService _settings;
    private readonly ConfigFileService _config;

    public RagBridgeService(SettingsService settings, ConfigFileService config)
    {
        _settings = settings;
        _config   = config;
    }

    public async Task ReindexAsync(IReadOnlyList<DocumentEntry> docs)
    {
        var url = _settings.Current.RagBridgeUrl;
        if (string.IsNullOrWhiteSpace(url)) return;

        try
        {
            var payload = new
            {
                documents = docs.Select(d => new
                {
                    id    = d.Name,
                    title = d.Name,
                    text  = d.Content,
                }).ToArray(),
            };

            using var req = BuildRequest(HttpMethod.Post, url.TrimEnd('/') + "/index", payload);
            using var resp = await _http.SendAsync(req);
            resp.EnsureSuccessStatusCode();
            var body = await resp.Content.ReadAsStringAsync();
            Log.Information("RAG reindex: {Body}", body);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "RAG bridge /index failed — will use full-doc fallback");
        }
    }

    public async Task<string?> RetrieveContextAsync(string query, int tokenBudget = 2_000)
    {
        var url = _settings.Current.RagBridgeUrl;
        if (string.IsNullOrWhiteSpace(url)) return null;

        try
        {
            var payload = new { query, top_k = 5, token_budget = tokenBudget };
            using var req = BuildRequest(HttpMethod.Post, url.TrimEnd('/') + "/retrieve", payload);
            using var resp = await _http.SendAsync(req);
            resp.EnsureSuccessStatusCode();

            var body = await resp.Content.ReadFromJsonAsync<RetrieveResponse>();
            if (body?.context is null) return null;

            Log.Information("RAG context: {Chars} chars from {Count} sources",
                body.context.Length, body.sources?.Length ?? 0);
            return body.context;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "RAG bridge /retrieve failed — using full-doc fallback");
            return null;
        }
    }

    public async Task<bool> IsHealthyAsync()
    {
        var url = _settings.Current.RagBridgeUrl;
        if (string.IsNullOrWhiteSpace(url)) return false;
        try
        {
            using var req = BuildRequest(HttpMethod.Get, url.TrimEnd('/') + "/health", null);
            using var resp = await _http.SendAsync(req);
            return resp.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Calls POST /clean on the ASP RAG bridge to clear in-memory indexes, chunks, and staged documents.
    /// </summary>
    public async Task<bool> ClearBridgeDataAsync()
    {
        var url = _settings.Current.RagBridgeUrl;
        if (string.IsNullOrWhiteSpace(url)) return true;

        try
        {
            var payload = new
            {
                namespaces = new[] { "chum_docs", "chum_session" },
                clean_staging = true
            };
            using var req = BuildRequest(HttpMethod.Post, url.TrimEnd('/') + "/clean", payload);
            using var resp = await _http.SendAsync(req);
            resp.EnsureSuccessStatusCode();
            Log.Information("RAG bridge data cleaned successfully");
            return true;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to call /clean on RAG bridge");
            return false;
        }
    }

    private HttpRequestMessage BuildRequest(HttpMethod method, string url, object? body)
    {
        var msg = new HttpRequestMessage(method, url);
        var key = _config.RagApiKey;
        if (!string.IsNullOrWhiteSpace(key))
            msg.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", key);
        if (body is not null)
            msg.Content = JsonContent.Create(body);
        return msg;
    }

    // ── Session memory (chum_session namespace) ──────────────────────────────

    // Full document list maintained in C# — the bridge replaces its entire corpus on each /index call,
    // so we must send everything every time to avoid erasing older session entries.
    private readonly List<SessionChunk> _sessionDocs = [];
    private readonly Lock _sessionLock = new();

    /// <summary>
    /// Upserts a document into the session namespace and re-indexes.
    /// Called after each Q&amp;A, vision event, or transcript chunk is ready.
    /// </summary>
    public async Task IndexSessionDocumentAsync(string id, string title, string text)
    {
        var url = _settings.Current.RagBridgeUrl;
        if (string.IsNullOrWhiteSpace(url)) return;

        SessionChunk[] snapshot;
        lock (_sessionLock)
        {
            _sessionDocs.RemoveAll(d => d.Id == id);
            _sessionDocs.Add(new SessionChunk(id, title, text));
            snapshot = [.. _sessionDocs];
        }

        try
        {
            var payload = new SessionIndexPayload(
                Namespace: "chum_session",
                Documents: snapshot.Select(c => new SessionDoc(c.Id, c.Title, c.Text)).ToArray());
            using var req = BuildRequest(HttpMethod.Post, url.TrimEnd('/') + "/index", payload);
            using var resp = await _http.SendAsync(req);
            resp.EnsureSuccessStatusCode();
            Log.Debug("Session RAG indexed: {N} documents (latest: {Id})", snapshot.Length, id);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Session RAG /index failed — document '{Id}' will not be retrievable", id);
        }
    }

    /// <summary>Clears the session index — call at the start of each capture session.</summary>
    public void ClearSessionIndex()
    {
        lock (_sessionLock) { _sessionDocs.Clear(); }
        var url = _settings.Current.RagBridgeUrl;
        if (string.IsNullOrWhiteSpace(url)) return;
        _ = Task.Run(async () =>
        {
            try
            {
                var payload = new SessionIndexPayload("chum_session", []);
                using var req = BuildRequest(HttpMethod.Post, url.TrimEnd('/') + "/index", payload);
                await _http.SendAsync(req);
            }
            catch { /* best-effort clear */ }
        });
    }

    public async Task<string?> RetrieveSessionContextAsync(string query, int tokenBudget = 1_000)
    {
        var url = _settings.Current.RagBridgeUrl;
        if (string.IsNullOrWhiteSpace(url)) return null;
        lock (_sessionLock) { if (_sessionDocs.Count == 0) return null; }

        try
        {
            var payload = new SessionRetrievePayload("chum_session", query, TopK: 5, TokenBudget: tokenBudget);
            using var req = BuildRequest(HttpMethod.Post, url.TrimEnd('/') + "/retrieve", payload);
            using var resp = await _http.SendAsync(req);
            resp.EnsureSuccessStatusCode();

            var body = await resp.Content.ReadFromJsonAsync<RetrieveResponse>();
            if (body?.context is null) return null;

            Log.Debug("Session RAG context: {Chars} chars from {Count} sources",
                body.context.Length, body.sources?.Length ?? 0);
            return body.context;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Session RAG /retrieve failed");
            return null;
        }
    }

    private sealed record RetrieveResponse(string? context, string[]? sources, int dropped_count);

    private sealed record SessionDoc(
        [property: System.Text.Json.Serialization.JsonPropertyName("id")]    string Id,
        [property: System.Text.Json.Serialization.JsonPropertyName("title")] string Title,
        [property: System.Text.Json.Serialization.JsonPropertyName("text")]  string Text);

    private sealed record SessionIndexPayload(
        [property: System.Text.Json.Serialization.JsonPropertyName("namespace")]  string Namespace,
        [property: System.Text.Json.Serialization.JsonPropertyName("documents")]  SessionDoc[] Documents);

    private sealed record SessionRetrievePayload(
        [property: System.Text.Json.Serialization.JsonPropertyName("namespace")]    string Namespace,
        [property: System.Text.Json.Serialization.JsonPropertyName("query")]        string Query,
        [property: System.Text.Json.Serialization.JsonPropertyName("top_k")]        int TopK,
        [property: System.Text.Json.Serialization.JsonPropertyName("token_budget")] int TokenBudget);
}

public sealed record SessionChunk(string Id, string Title, string Text);

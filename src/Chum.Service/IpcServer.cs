using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using Chum.Llm;
using Chum.Transcription;
using Microsoft.Extensions.Logging;

namespace Chum.Service;

/// <summary>
/// Named pipe server — accepts one Chum.Tray client at a time.
/// Receives QueryRequest/Pause/Resume from tray; streams tokens and status back.
/// </summary>
public sealed class IpcServer : IAsyncDisposable
{
    private const string PipeName = "ChumIPC";

    private readonly ILlmProvider _llm;
    private readonly ContextExtractor _context;
    private readonly AuditLogger _audit;
    private readonly ILogger<IpcServer> _log;
    private CancellationTokenSource _cts = new();
    private Task? _loop;

    public IpcServer(ILlmProvider llm, ContextExtractor context, AuditLogger audit, ILogger<IpcServer> log)
    {
        _llm = llm;
        _context = context;
        _audit = audit;
        _log = log;
    }

    public void Start()
    {
        _cts = new CancellationTokenSource();
        _loop = AcceptLoopAsync(_cts.Token);
    }

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var pipe = new NamedPipeServerStream(
                    PipeName,
                    PipeDirection.InOut,
                    maxNumberOfServerInstances: 1,
                    transmissionMode: PipeTransmissionMode.Byte,
                    options: PipeOptions.Asynchronous);

                await pipe.WaitForConnectionAsync(ct);
                _ = HandleClientAsync(pipe, ct);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _log.LogError(ex, "IPC accept error — retrying in 2s");
                await Task.Delay(2000, ct);
            }
        }
    }

    private async Task HandleClientAsync(NamedPipeServerStream pipe, CancellationToken ct)
    {
        using var _pipe = pipe;
        using var reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true);
        using var writer = new StreamWriter(pipe, Encoding.UTF8, leaveOpen: true) { AutoFlush = true };

        // Heartbeat — lets tray know service is alive
        await SendAsync(writer, new IpcMessage(IpcMessageType.Heartbeat, null, UtcMs()), ct);

        CancellationTokenSource? queryCts = null;

        while (pipe.IsConnected && !ct.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(ct);
            if (line is null) break;

            IpcMessage msg;
            try { msg = JsonSerializer.Deserialize<IpcMessage>(line)!; }
            catch { continue; }

            switch (msg.Type)
            {
                case IpcMessageType.QueryRequest:
                    queryCts?.Cancel();
                    queryCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    _ = StreamQueryAsync(msg, writer, queryCts.Token);
                    break;

                case IpcMessageType.CancelRequest:
                    queryCts?.Cancel();
                    break;

                case IpcMessageType.PauseRequest:
                    await SendStatusAsync(writer, "Paused", "PAUSED", "#EF4444", ct);
                    break;

                case IpcMessageType.ResumeRequest:
                    await SendStatusAsync(writer, "Listening", "Listening...", "#22C55E", ct);
                    break;
            }
        }

        queryCts?.Dispose();
    }

    private async Task StreamQueryAsync(IpcMessage msg, StreamWriter writer, CancellationToken ct)
    {
        var start = DateTimeOffset.UtcNow;
        await SendStatusAsync(writer, "Thinking", "Thinking...", "#3B82F6", ct);

        try
        {
            var payload = JsonSerializer.Deserialize<QueryRequestPayload>(msg.Payload ?? "{}");
            var request = new LlmRequest(
                PromptBuilder.BuildSystemPrompt(null),
                PromptBuilder.BuildUserMessage(payload?.ContextText ?? string.Empty),
                payload?.ImageBase64,
                payload?.ImageMediaType);

            int outputTokens = 0;
            await foreach (var token in _llm.StreamResponseAsync(request, ct))
            {
                outputTokens++;
                await SendAsync(writer,
                    new IpcMessage(IpcMessageType.TokenStream, token, UtcMs()), ct);
            }

            var latency = (long)(DateTimeOffset.UtcNow - start).TotalMilliseconds;
            _audit.LogQuery(_llm.ProviderName, 0, outputTokens, latency);
            await SendAsync(writer, new IpcMessage(IpcMessageType.StreamEnd, null, UtcMs()), ct);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _log.LogError(ex, "LLM error in service");
            await SendAsync(writer,
                new IpcMessage(IpcMessageType.StreamEnd, $"Error: {ex.Message}", UtcMs()), ct);
        }

        await SendStatusAsync(writer, "Listening", "Listening...", "#22C55E", ct);
    }

    private static async Task SendStatusAsync(StreamWriter w, string status, string text, string color, CancellationToken ct)
    {
        var payload = JsonSerializer.Serialize(new StatusUpdatePayload(status, text, color));
        await SendAsync(w, new IpcMessage(IpcMessageType.StatusUpdate, payload, UtcMs()), ct);
    }

    private static async Task SendAsync(StreamWriter w, IpcMessage msg, CancellationToken ct)
    {
        var line = JsonSerializer.Serialize(msg);
        await w.WriteLineAsync(line.AsMemory(), ct);
    }

    private static long UtcMs() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync();
        if (_loop is not null) await _loop.ConfigureAwait(false);
        _cts.Dispose();
    }
}

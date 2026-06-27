using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using Chum.Service;
using Microsoft.Extensions.Logging;

namespace Chum.App.Services;

/// <summary>
/// Connects Chum.Tray to ChumHostSvc over \\.\pipe\ChumIPC.
/// Fires events for incoming tokens, status updates, and heartbeats.
/// Reconnects automatically if the pipe drops (e.g. service restart).
/// </summary>
public sealed class IpcClient : IDisposable
{
    private const string PipeName = "ChumIPC";
    private const int ReconnectDelayMs = 2000;

    public event EventHandler<string>? TokenReceived;
    public event EventHandler? StreamEnded;
    public event EventHandler<StatusUpdatePayload>? StatusUpdated;
    public event EventHandler? Connected;
    public event EventHandler? Disconnected;

    private CancellationTokenSource _cts = new();
    private Task? _loop;
    private StreamWriter? _writer;
    private readonly Lock _writeLock = new();
    private bool _disposed;

    public bool IsConnected { get; private set; }

    public void Start()
    {
        _cts = new CancellationTokenSource();
        _loop = ConnectLoopAsync(_cts.Token);
    }

    private async Task ConnectLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var pipe = new NamedPipeClientStream(
                    ".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);

                await pipe.ConnectAsync(timeout: 5000, ct);
                IsConnected = true;
                Connected?.Invoke(this, EventArgs.Empty);

                using var reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true);
                var writer = new StreamWriter(pipe, Encoding.UTF8, leaveOpen: true) { AutoFlush = true };
                lock (_writeLock) _writer = writer;

                while (pipe.IsConnected && !ct.IsCancellationRequested)
                {
                    var line = await reader.ReadLineAsync(ct);
                    if (line is null) break;

                    IpcMessage msg;
                    try { msg = JsonSerializer.Deserialize<IpcMessage>(line)!; }
                    catch { continue; }

                    switch (msg.Type)
                    {
                        case IpcMessageType.TokenStream:
                            TokenReceived?.Invoke(this, msg.Payload ?? string.Empty);
                            break;
                        case IpcMessageType.StreamEnd:
                            StreamEnded?.Invoke(this, EventArgs.Empty);
                            break;
                        case IpcMessageType.StatusUpdate when msg.Payload is not null:
                            var status = JsonSerializer.Deserialize<StatusUpdatePayload>(msg.Payload);
                            if (status is not null) StatusUpdated?.Invoke(this, status);
                            break;
                    }
                }
            }
            catch (OperationCanceledException) { break; }
            catch { /* pipe not available yet — retry */ }
            finally
            {
                IsConnected = false;
                lock (_writeLock) _writer = null;
                Disconnected?.Invoke(this, EventArgs.Empty);
            }

            await Task.Delay(ReconnectDelayMs, ct).ConfigureAwait(false);
        }
    }

    public void SendQuery(string contextText, string? imageBase64 = null, string? imageMediaType = null)
    {
        var payload = JsonSerializer.Serialize(new QueryRequestPayload(contextText, imageBase64, imageMediaType));
        Send(new IpcMessage(IpcMessageType.QueryRequest, payload, UtcMs()));
    }

    public void SendCancel() => Send(new IpcMessage(IpcMessageType.CancelRequest, null, UtcMs()));
    public void SendPause() => Send(new IpcMessage(IpcMessageType.PauseRequest, null, UtcMs()));
    public void SendResume() => Send(new IpcMessage(IpcMessageType.ResumeRequest, null, UtcMs()));

    private void Send(IpcMessage msg)
    {
        lock (_writeLock)
        {
            if (_writer is null) return;
            try { _writer.WriteLine(JsonSerializer.Serialize(msg)); }
            catch { /* pipe dropped — ConnectLoop will reconnect */ }
        }
    }

    private static long UtcMs() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _cts.Cancel();
        _cts.Dispose();
    }
}

using Chum.Audio.Capture;
using Chum.Audio.Pipeline;
using Chum.Llm;
using Chum.Transcription;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Whisper.net.Ggml;

namespace Chum.Service;

/// <summary>
/// Windows Service worker — owns the audio pipeline, STT, transcript buffer,
/// and IPC server. Runs as ChumHostSvc; the WPF tray app connects over named pipe.
/// </summary>
public sealed class ChumWorker : BackgroundService
{
    private readonly ILogger<ChumWorker> _log;
    private readonly AuditLogger _audit;

    public ChumWorker(ILogger<ChumWorker> log, AuditLogger audit)
    {
        _log = log;
        _audit = audit;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        _log.LogInformation("ChumHostSvc starting");

        var apiKey = ReadApiKey();
        if (apiKey is null)
        {
            _log.LogError("No Anthropic API key found in Credential Manager — service idle");
            await Task.Delay(Timeout.Infinite, ct);
            return;
        }

        var modelDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Chum", "Models");
        Directory.CreateDirectory(modelDir);

        var loopback = new LoopbackCapture();
        var mic = new MicCapture();
        var pipeline = new AudioPipeline(loopback, mic);
        var stt = new WhisperSttEngine(modelDir, GgmlType.Small);
        var buffer = new TranscriptBuffer(TimeSpan.FromMinutes(10));
        var extractor = new ContextExtractor(buffer);
        ILlmProvider llm = new AnthropicLlmProvider(apiKey);

        stt.SegmentTranscribed += (_, seg) => buffer.Add(seg);

        await using var ipc = new IpcServer(llm, extractor, _audit,
            _log as ILogger<IpcServer> ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<IpcServer>.Instance);

        if (!stt.IsReady)
        {
            _log.LogInformation("Loading Whisper model...");
            await stt.InitializeAsync(ct: ct);
        }

        pipeline.Start();
        ipc.Start();
        _log.LogInformation("ChumHostSvc running — audio capture active, IPC listening on \\\\.\\pipe\\ChumIPC");

        // Transcription loop
        try
        {
            await foreach (var chunk in pipeline.Output.ReadAllAsync(ct))
            {
                try { await stt.TranscribeAsync(chunk.Samples, chunk.Source, ct); }
                catch (OperationCanceledException) { break; }
                catch (Exception ex) { _log.LogError(ex, "Transcription error — skipping segment"); }
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            pipeline.Stop();
            pipeline.Dispose();
            stt.Dispose();
            _log.LogInformation("ChumHostSvc stopped");
        }
    }

    private static string? ReadApiKey()
    {
        try
        {
            var cred = AdysTech.CredentialManager.CredentialManager.GetCredentials("Chum_Anthropic_ApiKey");
            return cred?.Password;
        }
        catch { return null; }
    }
}

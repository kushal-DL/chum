using System.Drawing;
using System.Threading.Channels;
using Chum.App.ViewModels;
using Timer = System.Threading.Timer;
using Chum.Audio.Models;
using Chum.Audio.Pipeline;
using Chum.Llm;
using Chum.Transcription;

namespace Chum.App.Services;

/// <summary>
/// Wires together the entire pipeline:
///   AudioPipeline → WhisperSttEngine → TranscriptBuffer
///   HotkeyService.QueryFired → ContextExtractor → ILlmProvider → OverlayViewModel
///
/// Runs the transcription consumer loop on a dedicated Task.
/// LLM calls are fully async — never block the UI thread.
/// </summary>
public sealed class MeetingOrchestrator : IDisposable
{
    private AudioPipeline _audio;
    private readonly WhisperSttEngine _stt;
    private readonly TranscriptBuffer _transcript;
    private readonly ContextExtractor _context;
    private readonly ILlmProvider _llm;
    private readonly HotkeyService _hotkeys;
    private readonly OverlayViewModel _overlay;
    private readonly SettingsService _settings;
    private readonly DxgiScreenCapture? _screenCapture;
    private readonly ClipboardMonitor? _clipboardMonitor;

    private readonly ScreenShareDetector _shareDetector;
    private readonly MeetingPlatformDetector _platformDetector;
    private bool _captureConfirming;
    private Timer? _captureConfirmTimer;
    private Timer? _gcTimer;
    private CancellationTokenSource _cts = new();

    /// <summary>Fires when a capture device unexpectedly disconnects. App should rebuild the audio pipeline.</summary>
    public event EventHandler? DeviceDisconnected;
    private Task? _transcriptionLoop;
    private bool _disposed;

    public MeetingOrchestrator(
        AudioPipeline audio,
        WhisperSttEngine stt,
        TranscriptBuffer transcript,
        ContextExtractor context,
        ILlmProvider llm,
        HotkeyService hotkeys,
        OverlayViewModel overlay,
        SettingsService settings,
        DxgiScreenCapture? screenCapture = null,
        ClipboardMonitor? clipboardMonitor = null)
    {
        _audio = audio;
        _stt = stt;
        _transcript = transcript;
        _context = context;
        _llm = llm;
        _hotkeys = hotkeys;
        _overlay = overlay;
        _settings = settings;
        _screenCapture = screenCapture;
        _clipboardMonitor = clipboardMonitor;

        if (_clipboardMonitor is not null)
            _clipboardMonitor.ImageAvailable += (_, _) => _overlay.SetClipboardPending(true);

        // Wire transcript segments to buffer
        _stt.SegmentTranscribed += (_, seg) =>
        {
            _transcript.Add(seg);
            _overlay.AddTranscriptLine($"[{seg.Timestamp:HH:mm:ss}] {seg.SpeakerLabel}: {seg.Text}");
        };

        // Wire screen-share auto-hide
        _shareDetector = new ScreenShareDetector();
        _shareDetector.SharingStateChanged += (_, isSharing) =>
        {
            if (!_settings.Current.AutoHideOnScreenShare) return;
            if (isSharing) _overlay.Hide();
            else _overlay.Show();
        };

        // Detect meeting platform for prompt context
        _platformDetector = new MeetingPlatformDetector();

        // Wire device disconnect → notify App to rebuild pipeline
        _audio.CaptureDisconnected += (_, _) =>
        {
            Serilog.Log.Warning("Audio capture device disconnected — requesting pipeline failover");
            _overlay.SetStatus(OverlayStatus.Initialising, "Audio device disconnected — switching to default...");
            DeviceDisconnected?.Invoke(this, EventArgs.Empty);
        };

        // Wire hotkeys
        _hotkeys.HoldStarted += (_, e) =>
        {
            if (e.ActionId == "HoldToAsk")
            {
                _overlay.SetListeningState(true);
                // High short beep on background thread — hook callback must return in <1ms
                _ = Task.Run(() => Console.Beep(880, 60));
            }
        };

        _hotkeys.QueryFired += async (_, e) =>
        {
            if (e.ActionId == "HoldToAsk")
            {
                // Lower beep signals end of capture, before async work begins
                _ = Task.Run(() => Console.Beep(660, 80));
                await HandleAudioQueryAsync(e.StartTime, e.EndTime);
            }
        };

        _hotkeys.HotkeyTapped += async (_, actionId) =>
        {
            if (actionId == "ActionItems")
                await HandleActionItemsQueryAsync();
            else if (actionId == "ScreenCapture")
                await TryHandleScreenCaptureAsync();
            else if (actionId == "PrivacyPause")
                TogglePause();
            else if (actionId == "HideOverlay")
                _overlay.ToggleVisibility();
        };
    }

    public async Task StartAsync()
    {
        _cts = new CancellationTokenSource();
        _audio.Start();
        _shareDetector.Start();
        _platformDetector.Start();
        _transcriptionLoop = RunTranscriptionLoopAsync(_cts.Token);
        _overlay.SetStatus(OverlayStatus.Listening, "Listening...");
        Serilog.Log.Information("MeetingOrchestrator started");

        // Periodic Gen2 GC every 10 min to reclaim memory after transcription bursts
        var interval = TimeSpan.FromMinutes(10);
        _gcTimer = new Timer(_ =>
        {
            GC.Collect(2, GCCollectionMode.Optimized, blocking: false);
            Serilog.Log.Debug("Periodic GC pass — WorkingSet={MB}MB",
                Environment.WorkingSet / 1_048_576);
        }, null, interval, interval);

        await Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        _gcTimer?.Dispose();
        _gcTimer = null;
        _audio.Stop();
        await _cts.CancelAsync();
        if (_transcriptionLoop is not null)
            await _transcriptionLoop.ConfigureAwait(false);
        _overlay.SetStatus(OverlayStatus.Idle, "Stopped");
        Serilog.Log.Information("MeetingOrchestrator stopped");
    }

    public void ReplaceAudio(AudioPipeline newPipeline)
    {
        _audio.Dispose();
        _audio = newPipeline;
    }

    public void Pause()
    {
        _audio.Pause();
        _overlay.SetStatus(OverlayStatus.Paused, "PAUSED — audio capture suspended");
    }

    public void Resume()
    {
        _audio.Resume();
        _overlay.SetStatus(OverlayStatus.Listening, "Listening...");
    }

    private void TogglePause()
    {
        if (_overlay.CurrentStatus == OverlayStatus.Paused) Resume();
        else Pause();
    }

    private async Task TryHandleScreenCaptureAsync()
    {
        if (_settings.Current.ConfirmScreenCapture && !_captureConfirming)
        {
            // First press: show confirmation banner and start a 5s auto-cancel timer
            _captureConfirming = true;
            _overlay.SetCapturePending(true);
            _captureConfirmTimer?.Dispose();
            _captureConfirmTimer = new Timer(_ =>
            {
                _captureConfirming = false;
                _overlay.SetCapturePending(false);
                Serilog.Log.Information("Screen capture confirmation timed out — cancelled");
            }, null, TimeSpan.FromSeconds(5), Timeout.InfiniteTimeSpan);
            return;
        }

        // Either confirmation is disabled, or this is the confirming second press
        _captureConfirming = false;
        _captureConfirmTimer?.Dispose();
        _captureConfirmTimer = null;
        _overlay.SetCapturePending(false);
        await HandleScreenCaptureQueryAsync();
    }

    private async Task RunTranscriptionLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await RunTranscriptionCycleAsync(ct);
                break; // Channel completed normally (stop was called)
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                Serilog.Log.Error(ex, "Transcription loop crashed — restarting in 2s");
                _overlay.SetStatus(OverlayStatus.Initialising, "Transcription restarting...");
                try { await Task.Delay(2000, ct); }
                catch (OperationCanceledException) { break; }
            }
        }
    }

    private async Task RunTranscriptionCycleAsync(CancellationToken ct)
    {
        if (!_stt.IsReady)
        {
            _overlay.SetStatus(OverlayStatus.Initialising, "Loading Whisper model...");
            await _stt.InitializeAsync(ct: ct);
        }

        _overlay.SetStatus(OverlayStatus.Listening, "Listening...");

        await foreach (var chunk in _audio.Output.ReadAllAsync(ct))
        {
            try
            {
                await _stt.TranscribeAsync(chunk.Samples, chunk.Source, ct);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                Serilog.Log.Error(ex, "Transcription error — skipping segment");
            }
        }
    }

    // Streams with exponential-backoff retry on LlmException (max 3 attempts: 1s, 2s, 4s gaps).
    private async Task StreamWithRetryAsync(LlmRequest request)
    {
        int[] retryDelaysMs = [1000, 2000, 4000];
        for (int attempt = 0; attempt <= retryDelaysMs.Length; attempt++)
        {
            try
            {
                await foreach (var token in _llm.StreamResponseAsync(request, _cts.Token))
                    _overlay.AppendResponseToken(token);
                return;
            }
            catch (OperationCanceledException) { throw; }
            catch (LlmException ex) when (attempt < retryDelaysMs.Length)
            {
                int delayMs = retryDelaysMs[attempt];
                Serilog.Log.Warning(ex, "LLM call failed (attempt {A}) — retrying in {D}ms", attempt + 1, delayMs);
                _overlay.SetStatus(OverlayStatus.Thinking, $"Retrying… ({attempt + 1}/{retryDelaysMs.Length})");
                await Task.Delay(delayMs, _cts.Token);
            }
            // Final attempt: LlmException propagates to caller
        }
    }

    public string GetTranscriptExportText()
    {
        var segments = _transcript.GetAll();
        if (segments.Count == 0) return string.Empty;
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Chum Emergency Transcript — {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine(new string('-', 60));
        foreach (var seg in segments)
            sb.AppendLine($"[{seg.Timestamp:HH:mm:ss}] {seg.SpeakerLabel}: {seg.Text}");
        return sb.ToString();
    }

    private async Task HandleAudioQueryAsync(DateTimeOffset holdStart, DateTimeOffset holdEnd)
    {
        _overlay.SetListeningState(false);
        _overlay.SetStatus(OverlayStatus.Thinking, $"Asking {_llm.ProviderName}…");
        _overlay.StartNewResponse();

        try
        {
            var contextText = _context.BuildContext(holdEnd, _settings.Current.MaxResponseTokens);
            var system = PromptBuilder.BuildSystemPrompt(_settings.Current.UserName,
                MeetingPlatformDetector.FriendlyName(_platformDetector.CurrentPlatform));
            var user = PromptBuilder.BuildUserMessage(contextText);
            var request = new LlmRequest(system, user,
                MaxTokens: _settings.Current.MaxResponseTokens,
                Temperature: _settings.Current.Temperature);

            Serilog.Log.Information("LLM request: provider={Provider} model={Model} type=AudioQuery",
                _llm.ProviderName, _llm.ModelId);

            await StreamWithRetryAsync(request);

            _overlay.SetStatus(OverlayStatus.Listening, "Listening...");
        }
        catch (LlmException ex)
        {
            _overlay.ShowError($"AI error: {ex.Message}");
            Serilog.Log.Error(ex, "LLM error during audio query");
            _overlay.SetStatus(OverlayStatus.Listening, "Listening...");
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _overlay.ShowError("Unexpected error — check logs");
            Serilog.Log.Error(ex, "Unexpected error in audio query");
        }
    }

    private async Task HandleActionItemsQueryAsync()
    {
        _overlay.SetStatus(OverlayStatus.Thinking, $"Extracting action items via {_llm.ProviderName}…");
        _overlay.StartNewResponse();

        try
        {
            var allSegments = _transcript.GetAll();
            if (allSegments.Count == 0)
            {
                _overlay.ShowError("No transcript yet — join a call first.");
                _overlay.SetStatus(OverlayStatus.Listening, "Listening...");
                return;
            }

            var sb = new System.Text.StringBuilder();
            foreach (var seg in allSegments)
                sb.AppendLine($"[{seg.Timestamp:HH:mm:ss}] {seg.SpeakerLabel}: \"{seg.Text}\"");

            var system = PromptBuilder.BuildSystemPrompt(_settings.Current.UserName,
                MeetingPlatformDetector.FriendlyName(_platformDetector.CurrentPlatform));
            var user = $"Extract all action items, decisions, and owners from this meeting transcript. Format as a bulleted list with owner names where identifiable.\n\n{sb}";
            var request = new LlmRequest(system, user, MaxTokens: 1024);

            Serilog.Log.Information("LLM request: provider={Provider} model={Model} type=ActionItems",
                _llm.ProviderName, _llm.ModelId);

            await StreamWithRetryAsync(request);

            _overlay.SetStatus(OverlayStatus.Listening, "Listening...");
        }
        catch (OperationCanceledException) { }
        catch (LlmException ex)
        {
            _overlay.ShowError($"AI error: {ex.Message}");
            Serilog.Log.Error(ex, "LLM error in action items query");
            _overlay.SetStatus(OverlayStatus.Listening, "Listening...");
        }
        catch (Exception ex)
        {
            _overlay.ShowError("Unexpected error — check logs");
            Serilog.Log.Error(ex, "Error in action items query");
            _overlay.SetStatus(OverlayStatus.Listening, "Listening...");
        }
    }

    public async Task HandleDroppedImageQueryAsync(string filePath)
    {
        _overlay.SetStatus(OverlayStatus.Thinking, "Loading dropped image...");
        _overlay.StartNewResponse();

        string? imageBase64;
        try
        {
            imageBase64 = await Task.Run(() =>
            {
                using var bmp = new Bitmap(filePath);
                return ImagePreprocessor.ToJpegBase64(bmp);
            });
        }
        catch (Exception ex)
        {
            _overlay.ShowError("Could not load the dropped image — is it a valid image file?");
            Serilog.Log.Error(ex, "Failed to load dropped image: {FilePath}", filePath);
            _overlay.SetStatus(OverlayStatus.Listening, "Listening...");
            return;
        }

        _overlay.SetStatus(OverlayStatus.Thinking, $"Analysing image via {_llm.ProviderName}…");

        try
        {
            var contextText = _context.BuildContext(DateTimeOffset.UtcNow, _settings.Current.MaxResponseTokens);
            var system = PromptBuilder.BuildSystemPrompt(_settings.Current.UserName,
                MeetingPlatformDetector.FriendlyName(_platformDetector.CurrentPlatform));
            var user = PromptBuilder.BuildUserMessage(contextText, hasImage: true);
            var request = new LlmRequest(system, user,
                ImageBase64: imageBase64,
                ImageMediaType: "image/jpeg",
                MaxTokens: _settings.Current.MaxResponseTokens);

            Serilog.Log.Information("LLM request: provider={Provider} model={Model} type=ImageDrop",
                _llm.ProviderName, _llm.ModelId);

            await StreamWithRetryAsync(request);

            _overlay.SetStatus(OverlayStatus.Listening, "Listening...");
        }
        catch (LlmException ex)
        {
            _overlay.ShowError($"AI error: {ex.Message}");
            Serilog.Log.Error(ex, "LLM error during dropped image query");
            _overlay.SetStatus(OverlayStatus.Listening, "Listening...");
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _overlay.ShowError("Unexpected error — check logs");
            Serilog.Log.Error(ex, "Unexpected error in dropped image query");
        }
    }

    private async Task HandleScreenCaptureQueryAsync()
    {
        _overlay.SetStatus(OverlayStatus.Thinking, "Capturing screen...");
        _overlay.StartNewResponse();

        // Clipboard image takes priority: user used Win+Shift+S or Snipping Tool.
        // Must be extracted here (UI thread) before any awaits.
        string? imageBase64 = null;
        if (_clipboardMonitor?.HasPendingImage == true)
        {
            imageBase64 = _clipboardMonitor.TryTakeImageAsJpegBase64(maxWidthPx: 1280, jpegQuality: 85);
            _overlay.SetClipboardPending(false);
            Serilog.Log.Information("Using clipboard image for screen capture query");
        }

        // Fall back to DXGI desktop duplication
        if (imageBase64 is null)
        {
            if (_screenCapture is null)
            {
                _overlay.ShowError("Screen capture is not available on this system (VM, RDP, or unsupported GPU).");
                _overlay.SetStatus(OverlayStatus.Listening, "Listening...");
                return;
            }

            try
            {
                imageBase64 = await Task.Run(() =>
                    _screenCapture.CaptureAsJpegBase64(maxWidthPx: 1280, jpegQuality: 85));
            }
            catch (Exception ex)
            {
                _overlay.ShowError("Screen capture failed — check logs");
                Serilog.Log.Error(ex, "Screen capture exception");
                _overlay.SetStatus(OverlayStatus.Listening, "Listening...");
                return;
            }

            if (imageBase64 is null)
            {
                _overlay.ShowError("Could not capture a frame. If Teams call content appears black, use Win+Shift+S to snip and then press Ctrl+Alt+S to analyse the clipboard image.");
                _overlay.SetStatus(OverlayStatus.Listening, "Listening...");
                return;
            }
        }

        _overlay.SetStatus(OverlayStatus.Thinking, $"Analysing screen via {_llm.ProviderName}…");

        try
        {
            var contextText = _context.BuildContext(DateTimeOffset.UtcNow, _settings.Current.MaxResponseTokens);
            var system = PromptBuilder.BuildSystemPrompt(_settings.Current.UserName,
                MeetingPlatformDetector.FriendlyName(_platformDetector.CurrentPlatform));
            var user = PromptBuilder.BuildUserMessage(contextText, hasImage: true);
            var request = new LlmRequest(system, user,
                ImageBase64: imageBase64,
                ImageMediaType: "image/jpeg",
                MaxTokens: _settings.Current.MaxResponseTokens);

            Serilog.Log.Information("LLM request: provider={Provider} model={Model} type=ScreenCapture",
                _llm.ProviderName, _llm.ModelId);

            await StreamWithRetryAsync(request);

            _overlay.SetStatus(OverlayStatus.Listening, "Listening...");
        }
        catch (LlmException ex)
        {
            _overlay.ShowError($"AI error: {ex.Message}");
            Serilog.Log.Error(ex, "LLM error during screen capture query");
            _overlay.SetStatus(OverlayStatus.Listening, "Listening...");
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _overlay.ShowError("Unexpected error — check logs");
            Serilog.Log.Error(ex, "Unexpected error in screen capture query");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _cts.Cancel();
        _cts.Dispose();
        _captureConfirmTimer?.Dispose();
        _gcTimer?.Dispose();
        _audio.Dispose();
        _stt.Dispose();
        _hotkeys.Dispose();
        _shareDetector.Dispose();
        _platformDetector.Dispose();
    }
}

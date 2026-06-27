using System.Drawing;
using System.Threading.Channels;
using Chum.App.ViewModels;
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
    private readonly AudioPipeline _audio;
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
    private CancellationTokenSource _cts = new();
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
                await HandleScreenCaptureQueryAsync();
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
        _transcriptionLoop = RunTranscriptionLoopAsync(_cts.Token);
        _overlay.SetStatus(OverlayStatus.Listening, "Listening...");
        Serilog.Log.Information("MeetingOrchestrator started");
        await Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        _audio.Stop();
        await _cts.CancelAsync();
        if (_transcriptionLoop is not null)
            await _transcriptionLoop.ConfigureAwait(false);
        _overlay.SetStatus(OverlayStatus.Idle, "Stopped");
        Serilog.Log.Information("MeetingOrchestrator stopped");
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

    private async Task RunTranscriptionLoopAsync(CancellationToken ct)
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

    private async Task HandleAudioQueryAsync(DateTimeOffset holdStart, DateTimeOffset holdEnd)
    {
        _overlay.SetListeningState(false);
        _overlay.SetStatus(OverlayStatus.Thinking, "Thinking...");
        _overlay.StartNewResponse();

        try
        {
            var contextText = _context.BuildContext(holdEnd, _settings.Current.MaxResponseTokens);
            var system = PromptBuilder.BuildSystemPrompt(_settings.Current.UserName);
            var user = PromptBuilder.BuildUserMessage(contextText);
            var request = new LlmRequest(system, user,
                MaxTokens: _settings.Current.MaxResponseTokens,
                Temperature: _settings.Current.Temperature);

            await foreach (var token in _llm.StreamResponseAsync(request, _cts.Token))
                _overlay.AppendResponseToken(token);

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
        _overlay.SetStatus(OverlayStatus.Thinking, "Extracting action items...");
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

            var system = PromptBuilder.BuildSystemPrompt(_settings.Current.UserName);
            var user = $"Extract all action items, decisions, and owners from this meeting transcript. Format as a bulleted list with owner names where identifiable.\n\n{sb}";
            var request = new LlmRequest(system, user, MaxTokens: 1024);

            await foreach (var token in _llm.StreamResponseAsync(request, _cts.Token))
                _overlay.AppendResponseToken(token);

            _overlay.SetStatus(OverlayStatus.Listening, "Listening...");
        }
        catch (Exception ex)
        {
            _overlay.ShowError($"Error: {ex.Message}");
            Serilog.Log.Error(ex, "Error in action items query");
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

        _overlay.SetStatus(OverlayStatus.Thinking, "Analysing image...");

        try
        {
            var contextText = _context.BuildContext(DateTimeOffset.UtcNow, _settings.Current.MaxResponseTokens);
            var system = PromptBuilder.BuildSystemPrompt(_settings.Current.UserName);
            var user = PromptBuilder.BuildUserMessage(contextText, hasImage: true);
            var request = new LlmRequest(system, user,
                ImageBase64: imageBase64,
                ImageMediaType: "image/jpeg",
                MaxTokens: _settings.Current.MaxResponseTokens);

            await foreach (var token in _llm.StreamResponseAsync(request, _cts.Token))
                _overlay.AppendResponseToken(token);

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

        _overlay.SetStatus(OverlayStatus.Thinking, "Analysing screen...");

        try
        {
            var contextText = _context.BuildContext(DateTimeOffset.UtcNow, _settings.Current.MaxResponseTokens);
            var system = PromptBuilder.BuildSystemPrompt(_settings.Current.UserName);
            var user = PromptBuilder.BuildUserMessage(contextText, hasImage: true);
            var request = new LlmRequest(system, user,
                ImageBase64: imageBase64,
                ImageMediaType: "image/jpeg",
                MaxTokens: _settings.Current.MaxResponseTokens);

            await foreach (var token in _llm.StreamResponseAsync(request, _cts.Token))
                _overlay.AppendResponseToken(token);

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
        _audio.Dispose();
        _stt.Dispose();
        _hotkeys.Dispose();
        _shareDetector.Dispose();
    }
}

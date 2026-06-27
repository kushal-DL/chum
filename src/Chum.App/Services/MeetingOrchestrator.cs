using System.Diagnostics;
using System.Drawing;
using System.Threading.Channels;
using Chum.App.ViewModels;
using Timer = System.Threading.Timer;
using Chum.Audio.Capture;
using Chum.Audio.Models;
using Chum.Audio.Pipeline;
using Chum.Llm;
using Chum.Transcription;

namespace Chum.App.Services;

public record AudioDeviceMismatchEventArgs(string DeviceId, string DeviceName, string PlatformName);

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
    private readonly OpenAiSttProvider? _cloudStt;
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
    private Timer? _latencyLogTimer;
    private readonly PipelineLatencyTracker _latencyTracker = new();
    private readonly SessionCostTracker _costTracker = new();
    private TemplateService? _templateService;
    private CancellationTokenSource? _cts;
    private MeetingPlatform _lastPlatform = MeetingPlatform.Unknown;

    public bool IsRunning => _cts != null;

    /// <summary>Fires when a capture device unexpectedly disconnects. App should rebuild the audio pipeline.</summary>
    public event EventHandler? DeviceDisconnected;
    /// <summary>Fires when a meeting app is detected opening (Unknown → known platform).</summary>
    public event EventHandler? MeetingAppOpened;
    /// <summary>Fires when a meeting app is detected closing (known → Unknown).</summary>
    public event EventHandler? MeetingAppClosed;
    /// <summary>Fires when Teams/Zoom is detected using a different audio device than Chum's current loopback.</summary>
    public event EventHandler<AudioDeviceMismatchEventArgs>? AudioDeviceMismatchDetected;
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
        ClipboardMonitor? clipboardMonitor = null,
        OpenAiSttProvider? cloudStt = null,
        TemplateService? templateService = null)
    {
        _audio = audio;
        _stt = stt;
        _cloudStt = cloudStt;
        _transcript = transcript;
        _context = context;
        _llm = llm;
        _hotkeys = hotkeys;
        _overlay = overlay;
        _settings = settings;
        _screenCapture = screenCapture;
        _clipboardMonitor = clipboardMonitor;
        _templateService = templateService;

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

        // Detect meeting platform for prompt context; fire lifecycle events for auto-start/stop
        _platformDetector = new MeetingPlatformDetector();
        _platformDetector.PlatformChanged += OnPlatformChanged;

        // Wire device disconnect → notify App to rebuild pipeline
        _audio.CaptureDisconnected += (_, _) =>
        {
            Serilog.Log.Warning("Audio capture device disconnected — requesting pipeline failover");
            _overlay.SetStatus(OverlayStatus.Initialising, "Audio device disconnected — switching to default...");
            DeviceDisconnected?.Invoke(this, EventArgs.Empty);
        };

        WireLevelMonitor(_audio);

        _latencyTracker.SlowTranscriptionDetected += (_, _) =>
            _overlay.ShowError("⚠ Transcription is slow (>15s) — consider using the 'base' Whisper model.");

        _llm.UsageRecorded += (_, usage) =>
        {
            _costTracker.Record(usage, _settings.Current.SpendThresholdDollars);
            _overlay.SetLastQueryCost(usage.InputTokens, usage.OutputTokens, usage.EstimatedCostUsd);
            Serilog.Log.Debug("LLM usage: ↑{In} ↓{Out} tokens, ~${Cost:F4}",
                usage.InputTokens, usage.OutputTokens, usage.EstimatedCostUsd);
        };
        _costTracker.ThresholdExceeded += (_, _) =>
        {
            var stats = _costTracker.GetStats();
            _overlay.ShowError($"⚠ Session API spend exceeded ${_settings.Current.SpendThresholdDollars:F2} (total: ${stats.TotalCostUsd:F4})");
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
            else if (actionId.StartsWith("Template", StringComparison.Ordinal) &&
                     int.TryParse(actionId["Template".Length..], out int tIdx))
                SwitchTemplate(tIdx);
        };
    }

    private void OnPlatformChanged(object? sender, MeetingPlatform platform)
    {
        bool wasInMeeting = _lastPlatform != MeetingPlatform.Unknown;
        bool isInMeeting  = platform != MeetingPlatform.Unknown;
        _lastPlatform = platform;

        // When Teams or Zoom is newly detected, check in background whether its audio device
        // matches Chum's current loopback source. Allow 3 s for the meeting app to start audio.
        if (!wasInMeeting && isInMeeting &&
            (platform == MeetingPlatform.Teams || platform == MeetingPlatform.Zoom))
            _ = Task.Run(() => CheckPlatformAudioDevice(platform));

        if (!_settings.Current.AutoStartCapture) return;

        if (!wasInMeeting && isInMeeting)
        {
            Serilog.Log.Information("Meeting app detected ({Platform}) — auto-starting capture",
                MeetingPlatformDetector.FriendlyName(platform));
            MeetingAppOpened?.Invoke(this, EventArgs.Empty);
        }
        else if (wasInMeeting && !isInMeeting)
        {
            Serilog.Log.Information("Meeting app closed — auto-stopping capture");
            MeetingAppClosed?.Invoke(this, EventArgs.Empty);
        }
    }

    private void CheckPlatformAudioDevice(MeetingPlatform platform)
    {
        // Wait for the meeting app to establish its audio session before querying COM
        System.Threading.Thread.Sleep(3000);

        string[] processNames = platform switch
        {
            MeetingPlatform.Teams => ["ms-teams", "teams", "teams2"],
            MeetingPlatform.Zoom  => ["zoom", "zoom.us"],
            _                     => []
        };

        var pids = processNames
            .SelectMany(n => Process.GetProcessesByName(n))
            .Select(p => p.Id)
            .ToHashSet();

        if (pids.Count == 0)
        {
            Serilog.Log.Verbose("CheckPlatformAudioDevice: no {Platform} processes found", platform);
            return;
        }

        if (!AudioSessionHelper.TryFindProcessRenderDevice(pids, out var deviceId, out var deviceName))
        {
            Serilog.Log.Verbose("CheckPlatformAudioDevice: no WASAPI session found for {Platform}", platform);
            return;
        }

        // Determine which device Chum's loopback is currently capturing from
        var chumDeviceId = string.IsNullOrEmpty(_settings.Current.LoopbackDeviceId)
            ? AudioSessionHelper.GetDefaultRenderDeviceId()
            : _settings.Current.LoopbackDeviceId;

        if (string.Equals(chumDeviceId, deviceId, StringComparison.OrdinalIgnoreCase))
        {
            Serilog.Log.Verbose("CheckPlatformAudioDevice: {Platform} audio device matches Chum loopback — no action needed", platform);
            return;
        }

        var platformName = MeetingPlatformDetector.FriendlyName(platform);
        Serilog.Log.Information(
            "{Platform} is using audio device '{Device}' (id={DeviceId}), differs from Chum loopback",
            platformName, deviceName, deviceId);

        AudioDeviceMismatchDetected?.Invoke(this, new AudioDeviceMismatchEventArgs(deviceId!, deviceName!, platformName));
    }

    public async Task StartAsync()
    {
        _cts = new CancellationTokenSource();
        _audio.Start();
        _shareDetector.Start();
        _platformDetector.Start();
        _transcriptionLoop = RunTranscriptionLoopAsync(_cts.Token);
        _overlay.SetStatus(OverlayStatus.Listening, "Listening...");
        if (_settings.Current.ShowDisclosureReminder)
            _overlay.ShowDisclosureReminder();
        Serilog.Log.Information("MeetingOrchestrator started");

        // Periodic Gen2 GC every 10 min to reclaim memory after transcription bursts
        var gcInterval = TimeSpan.FromMinutes(10);
        _gcTimer = new Timer(_ =>
        {
            GC.Collect(2, GCCollectionMode.Optimized, blocking: false);
            Serilog.Log.Debug("Periodic GC pass — WorkingSet={MB}MB",
                Environment.WorkingSet / 1_048_576);
        }, null, gcInterval, gcInterval);

        // Log STT latency percentiles every 5 min
        var latencyInterval = TimeSpan.FromMinutes(5);
        _latencyLogTimer = new Timer(_ =>
        {
            if (_latencyTracker.SegmentsRecorded > 0)
            {
                var (p50, p90, p99) = _latencyTracker.GetPercentiles();
                Serilog.Log.Information(
                    "STT latency (last {N} segments) — p50={P50:F1}s p90={P90:F1}s p99={P99:F1}s",
                    _latencyTracker.SegmentsRecorded, p50, p90, p99);
            }
            if (_latencyTracker.LlmQueriesRecorded > 0)
            {
                var (lp50, lp90, lp99) = _latencyTracker.GetLlmPercentiles();
                Serilog.Log.Information(
                    "LLM first-token latency (last {N} queries) — p50={P50:F0}ms p90={P90:F0}ms p99={P99:F0}ms",
                    _latencyTracker.LlmQueriesRecorded, lp50, lp90, lp99);
            }
        }, null, latencyInterval, latencyInterval);

        await Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        if (_cts is null) return;
        _gcTimer?.Dispose();
        _gcTimer = null;
        _latencyLogTimer?.Dispose();
        _latencyLogTimer = null;
        _audio.Stop();
        await _cts.CancelAsync();
        _cts = null;
        if (_transcriptionLoop is not null)
            await _transcriptionLoop.ConfigureAwait(false);
        _overlay.SetStatus(OverlayStatus.Idle, "Stopped");
        Serilog.Log.Information("MeetingOrchestrator stopped");
    }

    public void ReplaceAudio(AudioPipeline newPipeline)
    {
        _audio.Dispose();
        _audio = newPipeline;
        WireLevelMonitor(_audio);
    }

    private void WireLevelMonitor(AudioPipeline pipeline)
    {
        pipeline.LevelChanged += (_, e) =>
        {
            var pct = Math.Clamp((e.LevelDbFs + 60f) / 60f, 0d, 1d);
            if (e.Source == AudioSource.Loopback)
                _overlay.UpdateLoopbackLevel(pct, e.IsSpeech);
            else
                _overlay.UpdateMicLevel(pct, e.IsSpeech);
        };
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
            try
            {
                await _stt.InitializeAsync(ct: ct);
            }
            catch (Exception ex) when (_cloudStt != null)
            {
                Serilog.Log.Warning(ex, "Local Whisper init failed — running on cloud STT fallback");
                _overlay.ShowError("Local Whisper unavailable — using cloud STT (OpenAI Whisper)");
            }
        }

        _overlay.SetStatus(OverlayStatus.Listening, "Listening...");

        await foreach (var chunk in _audio.Output.ReadAllAsync(ct))
        {
            try
            {
                var sw = Stopwatch.StartNew();
                await TranscribeWithFallbackAsync(chunk.Samples, chunk.Source, ct);
                sw.Stop();
                _latencyTracker.Record(sw.Elapsed);
                Serilog.Log.Verbose("STT segment: {Duration:F1}s  e2e: {E2E:F1}s",
                    sw.Elapsed.TotalSeconds,
                    (DateTimeOffset.UtcNow - chunk.Timestamp).TotalSeconds);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                Serilog.Log.Error(ex, "Transcription error — skipping segment");
            }
        }
    }

    private async Task TranscribeWithFallbackAsync(float[] samples, AudioSource source, CancellationToken ct)
    {
        if (_stt.IsReady)
        {
            try
            {
                await _stt.TranscribeAsync(samples, source, ct);
                return;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) when (_cloudStt != null)
            {
                Serilog.Log.Warning(ex, "Local STT failed — retrying via cloud fallback");
            }
        }

        if (_cloudStt != null)
            await _cloudStt.TranscribeAsync(samples, source, ct);
        else
            await _stt.TranscribeAsync(samples, source, ct); // will throw with clear message
    }

    // Streams with exponential-backoff retry on LlmException (max 3 attempts: 1s, 2s, 4s gaps).
    // onFirstToken fires with elapsed time at the moment the first token is received.
    private async Task StreamWithRetryAsync(LlmRequest request, Action<TimeSpan>? onFirstToken = null)
    {
        var ct = _cts?.Token ?? CancellationToken.None;
        int[] retryDelaysMs = [1000, 2000, 4000];
        for (int attempt = 0; attempt <= retryDelaysMs.Length; attempt++)
        {
            try
            {
                var sw = Stopwatch.StartNew();
                bool firstToken = true;
                await foreach (var token in _llm.StreamResponseAsync(request, ct))
                {
                    if (firstToken)
                    {
                        onFirstToken?.Invoke(sw.Elapsed);
                        firstToken = false;
                    }
                    _overlay.AppendResponseToken(token);
                }
                return;
            }
            catch (OperationCanceledException) { throw; }
            catch (LlmException ex) when (attempt < retryDelaysMs.Length)
            {
                int delayMs = retryDelaysMs[attempt];
                Serilog.Log.Warning(ex, "LLM call failed (attempt {A}) — retrying in {D}ms", attempt + 1, delayMs);
                _overlay.SetStatus(OverlayStatus.Thinking, $"Retrying… ({attempt + 1}/{retryDelaysMs.Length})");
                await Task.Delay(delayMs, ct);
            }
            // Final attempt: LlmException propagates to caller
        }
    }

    public (int Segments, double SttP50Ms, double SttP90Ms, double SttP99Ms,
            int LlmQueries, double LlmP50Ms, double LlmP90Ms, double LlmP99Ms) GetLatencyStats()
    {
        var (p50, p90, p99) = _latencyTracker.GetPercentiles();
        var (lp50, lp90, lp99) = _latencyTracker.GetLlmPercentiles();
        return (_latencyTracker.SegmentsRecorded, p50 * 1000, p90 * 1000, p99 * 1000,
                _latencyTracker.LlmQueriesRecorded, lp50, lp90, lp99);
    }

    public (int Queries, int InputTokens, int OutputTokens, decimal TotalCostUsd)
        GetCostStats()
    {
        var s = _costTracker.GetStats();
        return (s.Queries, s.InputTokens, s.OutputTokens, s.TotalCostUsd);
    }

    public TemplateService? GetTemplateService() => _templateService;

    private void SwitchTemplate(int oneBasedIndex)
    {
        if (_templateService is null) return;
        var all = _templateService.All;
        int idx = oneBasedIndex - 1;
        if (idx < 0 || idx >= all.Count) return;
        var t = all[idx];
        _settings.Current.ActiveTemplateName = t.Name;
        _settings.Save(); // persist so the setting survives restart
        _overlay.SetStatus(OverlayStatus.Idle, $"Template: {t.Name}");
        Serilog.Log.Information("Active template switched to '{Name}'", t.Name);
    }

    public string GetSttAccelerationMode() => _stt.AccelerationMode;

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
                MeetingPlatformDetector.FriendlyName(_platformDetector.CurrentPlatform),
                _stt.DetectedLanguage,
                _templateService?.GetByName(_settings.Current.ActiveTemplateName));
            var user = PromptBuilder.BuildUserMessage(contextText);
            var request = new LlmRequest(system, user,
                MaxTokens: _settings.Current.MaxResponseTokens,
                Temperature: _settings.Current.Temperature);

            Serilog.Log.Information("LLM request: provider={Provider} model={Model} type=AudioQuery",
                _llm.ProviderName, _llm.ModelId);

            await StreamWithRetryAsync(request, t => _latencyTracker.RecordLlmLatency(t));

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
                MeetingPlatformDetector.FriendlyName(_platformDetector.CurrentPlatform),
                _stt.DetectedLanguage,
                _templateService?.GetByName(_settings.Current.ActiveTemplateName));
            var user = $"Extract all action items, decisions, and owners from this meeting transcript. Format as a bulleted list with owner names where identifiable.\n\n{sb}";
            var request = new LlmRequest(system, user, MaxTokens: 1024);

            Serilog.Log.Information("LLM request: provider={Provider} model={Model} type=ActionItems",
                _llm.ProviderName, _llm.ModelId);

            await StreamWithRetryAsync(request, t => _latencyTracker.RecordLlmLatency(t));

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
                MeetingPlatformDetector.FriendlyName(_platformDetector.CurrentPlatform),
                _stt.DetectedLanguage,
                _templateService?.GetByName(_settings.Current.ActiveTemplateName));
            var user = PromptBuilder.BuildUserMessage(contextText, hasImage: true);
            var request = new LlmRequest(system, user,
                ImageBase64: imageBase64,
                ImageMediaType: "image/jpeg",
                MaxTokens: _settings.Current.MaxResponseTokens);

            Serilog.Log.Information("LLM request: provider={Provider} model={Model} type=ImageDrop",
                _llm.ProviderName, _llm.ModelId);

            await StreamWithRetryAsync(request, t => _latencyTracker.RecordLlmLatency(t));

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
                MeetingPlatformDetector.FriendlyName(_platformDetector.CurrentPlatform),
                _stt.DetectedLanguage,
                _templateService?.GetByName(_settings.Current.ActiveTemplateName));
            var user = PromptBuilder.BuildUserMessage(contextText, hasImage: true);
            var request = new LlmRequest(system, user,
                ImageBase64: imageBase64,
                ImageMediaType: "image/jpeg",
                MaxTokens: _settings.Current.MaxResponseTokens);

            Serilog.Log.Information("LLM request: provider={Provider} model={Model} type=ScreenCapture",
                _llm.ProviderName, _llm.ModelId);

            await StreamWithRetryAsync(request, t => _latencyTracker.RecordLlmLatency(t));

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
        _cts?.Cancel();
        _cts?.Dispose();
        _captureConfirmTimer?.Dispose();
        _gcTimer?.Dispose();
        _latencyLogTimer?.Dispose();
        _audio.Dispose();
        _stt.Dispose();
        _cloudStt?.Dispose();
        _hotkeys.Dispose();
        _shareDetector.Dispose();
        _platformDetector.Dispose();
    }
}

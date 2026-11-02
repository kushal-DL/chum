using System.Diagnostics;
using System.Drawing;
using System.Threading.Channels;
using Chum.App.Models;
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
    private ISttEngine _stt;
    private readonly TranscriptBuffer _transcript;
    private readonly ContextExtractor _context;
    private readonly ILlmProvider _llm;
    private readonly HotkeyService _hotkeys;
    private readonly OverlayViewModel _overlay;
    private readonly SettingsService _settings;
    private readonly ConfigFileService _config;
    private readonly DxgiScreenCapture? _screenCapture;
    private readonly ClipboardMonitor? _clipboardMonitor;

    private readonly ScreenShareDetector _shareDetector;
    private readonly MeetingPlatformDetector _platformDetector;
    private readonly TeamsCaptionsReader _captionsReader = new();
    private bool _captureConfirming;
    private Timer? _captureConfirmTimer;
    private Timer? _gcTimer;
    private Timer? _latencyLogTimer;
    private readonly PipelineLatencyTracker _latencyTracker = new();
    private readonly SessionCostTracker _costTracker = new();
    private TemplateService? _templateService;
    private readonly DocumentContextService? _docContext;
    private readonly RagBridgeService? _rag;
    private SessionJournal? _journal;
    private Timer? _transcriptJournalTimer;
    private DateTimeOffset _lastTranscriptChunkTime = DateTimeOffset.MinValue;
    private CancellationTokenSource? _cts;
    // Separate scope for the current LLM response so the user can stop a reply without
    // stopping the transcription pipeline (which lives on _cts).
    private CancellationTokenSource? _responseCts;
    // UI record-toggle state (button-driven; parallels the HoldToAsk hotkey toggle).
    private bool _uiRecording;
    private readonly QaTurnHistory _qaHistory = new(5);
    private string? _lastQueryText;
    private DateTimeOffset _lastQueryHoldEnd;
    private bool _lastQueryUsedRag;

    private static readonly HashSet<string> WhisperHallucinationPhrases = new(StringComparer.OrdinalIgnoreCase)
    {
        "thank you",
        "thank you.",
        "thank you!",
        "thank you very much",
        "thank you very much.",
        "thanks for watching",
        "thanks for watching.",
        "i don't know",
        "i don't know.",
        "i don't know?",
        "bye",
        "bye.",
        "you",
        "you."
    };

    public static bool IsWhisperHallucination(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return true;
        var trimmed = text.Trim();
        if (trimmed == "." || trimmed == "..." || trimmed == "!" || trimmed == "?") return true;
        if (WhisperHallucinationPhrases.Contains(trimmed)) return true;
        var stripped = trimmed.TrimEnd('.', '!', '?', ' ');
        if (WhisperHallucinationPhrases.Contains(stripped)) return true;
        return false;
    }

    public bool IsRunning => _cts != null;

    /// <summary>Fires when a capture device unexpectedly disconnects. App should rebuild the audio pipeline.</summary>
    public event EventHandler? DeviceDisconnected;
    /// <summary>Fires when a meeting app is detected opening (Unknown → known platform).</summary>
    public event EventHandler? MeetingAppOpened;
    /// <summary>Fires when a meeting app is detected closing (known → Unknown).</summary>
    public event EventHandler? MeetingAppClosed;
    /// <summary>Fires when Teams/Zoom is detected using a different audio device than Chum's current loopback.</summary>
    public event EventHandler<AudioDeviceMismatchEventArgs>? AudioDeviceMismatchDetected;
    /// <summary>Fires when the user invokes the snip-mode hotkey — subscriber (App) should show the snip UI.</summary>
    public event EventHandler? SnipModeRequested;
    private Task? _transcriptionLoop;
    private bool _disposed;

    public MeetingOrchestrator(
        AudioPipeline audio,
        ISttEngine stt,
        TranscriptBuffer transcript,
        ContextExtractor context,
        ILlmProvider llm,
        HotkeyService hotkeys,
        OverlayViewModel overlay,
        SettingsService settings,
        ConfigFileService config,
        DxgiScreenCapture? screenCapture = null,
        ClipboardMonitor? clipboardMonitor = null,
        TemplateService? templateService = null,
        DocumentContextService? docContext = null,
        RagBridgeService? rag = null)
    {
        _audio = audio;
        _stt = stt;
        _transcript = transcript;
        _context = context;
        _llm = llm;
        _hotkeys = hotkeys;
        _overlay = overlay;
        _settings = settings;
        _config = config;
        _screenCapture = screenCapture;
        _clipboardMonitor = clipboardMonitor;
        _templateService = templateService;
        _docContext = docContext;
        _rag = rag;

        if (_clipboardMonitor is not null)
            _clipboardMonitor.ImageAvailable += (_, _) => _overlay.SetClipboardPending(true);

        // Wire transcript segments to buffer and session journal
        _stt.SegmentTranscribed += (_, seg) =>
        {
            if (IsWhisperHallucination(seg.Text))
            {
                Serilog.Log.Debug("Discarded Whisper silence hallucination: '{Text}'", seg.Text);
                return;
            }
            _transcript.Add(seg);
            _overlay.AddTranscriptLine($"[{seg.Timestamp:HH:mm:ss}] {seg.SpeakerLabel}: {seg.Text}");
            _journal?.WriteSegments([seg]);
        };

        // Feed Teams caption lines into the transcript buffer as supplementary segments
        _captionsReader.CaptionLineReceived += (_, text) =>
        {
            var seg = new Chum.Transcription.Models.TranscriptSegment(
                DateTimeOffset.UtcNow, AudioSource.Loopback, $"[Teams caption] {text}");
            _transcript.Add(seg);
            _overlay.AddTranscriptLine($"[{seg.Timestamp:HH:mm:ss}] Caption: {seg.Text}");
        };

        // Wire screen-share auto-hide; restore is delayed 2 s to prevent flicker if the
        // sharing window briefly disappears during an application switch.
        _shareDetector = new ScreenShareDetector();
        _shareDetector.SharingStateChanged += (_, isSharing) =>
        {
            if (!_settings.Current.AutoHideOnScreenShare) return;
            // When the overlay is capture-excluded (WDA_EXCLUDEFROMCAPTURE) it is already invisible
            // to meeting participants and recordings, so auto-hiding it just forces the user to
            // re-open it from the tray after every share — skip it. Only auto-hide when the user
            // has turned capture exclusion off (i.e. the overlay would genuinely be visible).
            if (_settings.Current.ExcludeFromScreenCapture) return;
            if (isSharing)
            {
                _overlay.Hide();
            }
            else
            {
                _ = Task.Delay(TimeSpan.FromSeconds(2)).ContinueWith(_ =>
                {
                    if (!_shareDetector.IsSharing)
                        _overlay.Show();
                }, TaskScheduler.Default);
            }
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

        void OnUsage(object? _, LlmUsage usage)
        {
            _costTracker.Record(usage, _settings.Current.SpendThresholdDollars);
            _overlay.SetLastQueryCost(usage.InputTokens, usage.OutputTokens, usage.EstimatedCostUsd);
            Serilog.Log.Debug("LLM usage: ↑{In} ↓{Out} tokens, ~${Cost:F4}",
                usage.InputTokens, usage.OutputTokens, usage.EstimatedCostUsd);
        }
        _llm.UsageRecorded += OnUsage;
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
                _overlay.SetStatus(OverlayStatus.Listening, "Recording… (press Ctrl+Alt+Space to send)");
                _audio.StartRawRecording();
            }
        };

        _hotkeys.QueryFired += async (_, e) =>
        {
            if (e.ActionId == "HoldToAsk")
            {
                var recorded = _audio.StopRawRecording();
                await HandleAudioQueryAsync(recorded, e.EndTime);
            }
        };

        _hotkeys.HotkeyTapped += async (_, actionId) =>
        {
            if (actionId == "ActionItems")
                await HandleActionItemsQueryAsync();
            else if (actionId == "ScreenCapture")
                await TryHandleScreenCaptureAsync();
            else if (actionId == "SnipCapture")
                SnipModeRequested?.Invoke(this, EventArgs.Empty);
            else if (actionId == "PrivacyPause")
                TogglePause();
            else if (actionId == "HideOverlay")
                _overlay.ToggleVisibility();
            else if (actionId == "CycleResponseMode")
                _overlay.CycleResponseMode();
            else if (actionId.StartsWith("Category", StringComparison.Ordinal) &&
                     int.TryParse(actionId["Category".Length..], out int cIdx))
                _overlay.SelectCategoryByIndex(cIdx - 1);
            else if (actionId.StartsWith("Template", StringComparison.Ordinal) &&
                     int.TryParse(actionId["Template".Length..], out int tIdx))
                SwitchTemplate(tIdx);
        };

        _overlay.CategoryChanged += OnCategoryChanged;
        _ = RefreshCategoriesAsync();
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

        // Start/stop Teams caption reader when the platform changes to/from Teams
        if (platform == MeetingPlatform.Teams && _settings.Current.UseTeamsCaptions)
            _captionsReader.Start();
        else if (_lastPlatform == MeetingPlatform.Teams || platform != MeetingPlatform.Teams)
            _captionsReader.Stop();

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

        var platformName = MeetingPlatformDetector.FriendlyName(platform);
        string? deviceId = null;
        string? deviceName = null;

        // Zoom installs a named virtual audio device — check by FriendlyName first.
        // Fall through to PID-based detection if the virtual device is not present.
        if (platform == MeetingPlatform.Zoom &&
            AudioSessionHelper.TryFindRenderDeviceByName("Zoom Audio Device", out deviceId, out deviceName))
        {
            Serilog.Log.Information("Zoom Audio Device found: '{Device}'", deviceName);
        }
        else
        {
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

            if (!AudioSessionHelper.TryFindProcessRenderDevice(pids, out deviceId, out deviceName))
            {
                Serilog.Log.Verbose("CheckPlatformAudioDevice: no WASAPI session found for {Platform}", platform);
                return;
            }
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
        _ = RefreshCategoriesAsync();

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

        // Session memory: journal and periodic RAG chunk indexing
        _journal = new SessionJournal();
        _journal.Start();
        _rag?.ClearSessionIndex();
        _lastTranscriptChunkTime = DateTimeOffset.UtcNow;
        var journalInterval = TimeSpan.FromSeconds(120);
        _transcriptJournalTimer = new Timer(_ => _ = JournalTranscriptTickAsync(),
            null, journalInterval, journalInterval);

        await Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        if (_cts is null) return;
        _gcTimer?.Dispose();
        _gcTimer = null;
        _latencyLogTimer?.Dispose();
        _latencyLogTimer = null;
        _transcriptJournalTimer?.Dispose();
        _transcriptJournalTimer = null;
        _audio.Stop();
        await _cts.CancelAsync();
        _cts = null;
        if (_transcriptionLoop is not null)
            await _transcriptionLoop.ConfigureAwait(false);

        // Final flush: write remaining segments and index any pending transcript chunk
        if (_journal is { IsStarted: true })
        {
            await IndexTranscriptChunkAsync();
            _journal.Stop();
            _journal = null;
        }

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

    /// <summary>
    /// UI record toggle: first call starts raw recording, second call stops it and sends
    /// the question to the LLM WITHOUT RAG context.
    /// </summary>
    public void ToggleRecording()
    {
        if (!_uiRecording)
        {
            _uiRecording = true;
            _overlay.SetRecording(true);
            _overlay.SetListeningState(true);
            _overlay.SetStatus(OverlayStatus.Listening, "Recording… (● again to send, RAG to send with context)");
            _audio.StartRawRecording();
        }
        else
        {
            _uiRecording = false;
            _overlay.SetRecording(false);
            var recorded = _audio.StopRawRecording();
            _ = HandleAudioQueryAsync(recorded, DateTimeOffset.UtcNow, useRag: false);
        }
    }

    /// <summary>
    /// RAG button: stops an active UI recording and sends the question to the LLM WITH RAG
    /// context (document retrieval + session memory). No-op if not currently recording.
    /// </summary>
    public void StopRecordingWithRag()
    {
        if (!_uiRecording) return;
        _uiRecording = false;
        _overlay.SetRecording(false);
        var recorded = _audio.StopRawRecording();
        _ = HandleAudioQueryAsync(recorded, DateTimeOffset.UtcNow, useRag: true);
    }

    /// <summary>Stops the current LLM response without affecting the transcription pipeline.</summary>
    public void CancelResponse()
    {
        _responseCts?.Cancel();
        _overlay.SetListeningState(false); // clears the streaming indicator
        _overlay.SetStatus(OverlayStatus.Listening, "Response stopped");
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
                var sw = Stopwatch.StartNew();
                await _stt.TranscribeAsync(chunk.Samples, chunk.Source, ct);
                sw.Stop();
                _latencyTracker.Record(sw.Elapsed);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                Serilog.Log.Error(ex, "Transcription error — skipping segment");
            }
        }
    }

    // Streams with exponential-backoff retry on LlmException (max 3 attempts: 1s, 2s, 4s gaps).
    // onFirstToken fires with elapsed time at the moment the first token is received.
    private async Task<string> StreamWithRetryAsync(ILlmProvider llm, LlmRequest request, Action<TimeSpan>? onFirstToken = null)
    {
        // Each response gets its own cancellation scope (linked to the pipeline token) so
        // "Stop response" cancels only the reply, leaving transcription running.
        _responseCts?.Dispose();
        _responseCts = CancellationTokenSource.CreateLinkedTokenSource(_cts?.Token ?? CancellationToken.None);
        var ct = _responseCts.Token;
        int[] retryDelaysMs = [1000, 2000, 4000];
        for (int attempt = 0; attempt <= retryDelaysMs.Length; attempt++)
        {
            try
            {
                var sw = Stopwatch.StartNew();
                var accumulated = new System.Text.StringBuilder();
                bool firstToken = true;
                await foreach (var token in llm.StreamResponseAsync(request, ct))
                {
                    if (firstToken)
                    {
                        onFirstToken?.Invoke(sw.Elapsed);
                        firstToken = false;
                    }
                    accumulated.Append(token);
                    _overlay.AppendResponseToken(token);
                }
                return accumulated.ToString();
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
        return string.Empty; // unreachable in practice
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

    private static readonly System.Net.Http.HttpClient _googleSearchHttp =
        new(new System.Net.Http.SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(10) })
        { Timeout = TimeSpan.FromSeconds(60) };

    /// <summary>
    /// Captures the given screen region and sends it to the local Google AI Search bridge
    /// (start-internet-search.ps1 must be running; URL configured in Settings → Google AI Search).
    /// </summary>
    public async Task HandleGoogleSearchAsync(System.Drawing.Rectangle region)
    {
        _overlay.SetStatus(OverlayStatus.Thinking, "Capturing region for Google AI Search…");
        _overlay.StartNewResponse();

        // Hide the overlay HWND before capturing. WDA_EXCLUDEFROMCAPTURE (applied when
        // ExcludeFromScreenCapture is on) makes our window appear solid black in DXGI/GDI
        // captures even when the content is transparent. Hiding the HWND removes the window
        // from DWM composition entirely. GDI CaptureRegionViaGdi is static — no DXGI needed.
        _overlay.HideForCapture();
        string? imageBase64 = null;
        try
        {
            await Task.Delay(200); // allow DWM to recomposite without the overlay
            imageBase64 = await Task.Run(() =>
                DxgiScreenCapture.CaptureRegionViaGdi(region, maxWidthPx: 1920, jpegQuality: 90));
            Serilog.Log.Information(
                "Google Search GDI capture: region={Region} imageNull={Null} imageLen={Len}",
                region, imageBase64 is null, imageBase64?.Length ?? 0);
        }
        catch (Exception ex)
        {
            _overlay.ShowError("Region capture failed — check logs");
            Serilog.Log.Error(ex, "Google Search region capture exception");
            _overlay.SetStatus(OverlayStatus.Listening, "Listening...");
            return;
        }
        finally
        {
            _overlay.RestoreAfterCapture();
        }

        if (string.IsNullOrEmpty(imageBase64))
        {
            _overlay.ShowError("Could not capture selected region.");
            _overlay.SetStatus(OverlayStatus.Listening, "Listening...");
            return;
        }

        _overlay.SetStatus(OverlayStatus.Thinking, "Sending to Google AI Search…");

        try
        {
            var payload = System.Text.Json.JsonSerializer.Serialize(new { image_base64 = imageBase64 });
            using var req = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Post,
                _settings.Current.InternetSearchBaseUrl.TrimEnd('/') + "/image");
            req.Content = new System.Net.Http.StringContent(payload, System.Text.Encoding.UTF8, "application/json");
            var apiKey = _config.InternetSearchApiKey;
            if (!string.IsNullOrWhiteSpace(apiKey))
                req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
            var response = await _googleSearchHttp.SendAsync(req);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var text = doc.RootElement.GetProperty("response").GetString() ?? "No response";
            if (_journal is not null)
                _ = JournalVisionAsync(imageBase64, text, "google_search");

            // Simulate streaming for UX consistency — chunk into 8-char pieces
            for (int i = 0; i < text.Length; i += 8)
            {
                _overlay.AppendResponseToken(text.Substring(i, Math.Min(8, text.Length - i)));
                await Task.Delay(12);
            }
            _overlay.SetStatus(OverlayStatus.Listening, "Listening...");
        }
        catch (Exception ex)
        {
            _overlay.ShowError("Google AI Search not reachable — is start-internet-search.ps1 running?");
            Serilog.Log.Error(ex, "Google Search API error");
            _overlay.SetStatus(OverlayStatus.Listening, "Listening...");
        }
    }

    // ── Capture session batch sends ───────────────────────────────────────

    /// <summary>
    /// Captures the locked session region, stores the result, and triggers a border flash.
    /// Returns the JPEG base64 string, or null if capture failed.
    /// </summary>
    public async Task<(string? ImageBase64, System.Windows.Media.Imaging.BitmapSource? Thumbnail)>
        CaptureSessionShotAsync(System.Drawing.Rectangle region)
    {
        _overlay.HideForCapture();
        string? imageBase64 = null;
        try
        {
            await Task.Delay(200); // allow DWM to recomposite
            imageBase64 = await Task.Run(() =>
                DxgiScreenCapture.CaptureRegionViaGdi(region, maxWidthPx: 1920, jpegQuality: 85));
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Capture session shot failed");
            return (null, null);
        }
        finally
        {
            _overlay.RestoreAfterCapture();
        }

        if (imageBase64 is null) return (null, null);

        // Build a small thumbnail BitmapSource for the overlay thumbnail strip
        System.Windows.Media.Imaging.BitmapSource? thumbnail = null;
        try
        {
            var bytes = Convert.FromBase64String(imageBase64);
            thumbnail = await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                using var ms = new System.IO.MemoryStream(bytes);
                var bmp = new System.Windows.Media.Imaging.BitmapImage();
                bmp.BeginInit();
                bmp.StreamSource = ms;
                bmp.DecodePixelWidth = 68;
                bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                bmp.EndInit();
                bmp.Freeze();
                return (System.Windows.Media.Imaging.BitmapSource)bmp;
            });
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "Failed to create session shot thumbnail");
        }

        return (imageBase64, thumbnail);
    }

    /// <summary>Sends all queued session shots to the configured LLM.</summary>
    public async Task HandleCaptureSessionLlmAsync(IReadOnlyList<string> images)
    {
        if (images.Count == 0)
        {
            _overlay.ShowError("No shots in session — add at least one before sending.");
            return;
        }

        var visionLlm = GetLlm(requiresVision: true);
        _overlay.SetStatus(OverlayStatus.Thinking, $"Sending {images.Count} shot(s) to {visionLlm.ModelId}…");
        _overlay.StartNewResponse();

        try
        {
            var contextText = _context.BuildContext(DateTimeOffset.UtcNow, _settings.Current.MaxResponseTokens);
            var system = PromptBuilder.BuildSystemPrompt(_settings.Current.UserName,
                MeetingPlatformDetector.FriendlyName(_platformDetector.CurrentPlatform),
                _stt.DetectedLanguage,
                _templateService?.GetByName(_settings.Current.ActiveTemplateName));
            var modeHint = GetModeInstruction();
            if (!string.IsNullOrEmpty(modeHint))
                system = system + "\n\n" + modeHint;

            var user = $"Here are {images.Count} screenshot{(images.Count == 1 ? "" : "s")} of the same screen region, " +
                       "taken sequentially. Analyze and respond as per the instructions/question shown in the screenshots.";
            if (!string.IsNullOrWhiteSpace(contextText))
                user += $"\n\nMeeting context:\n{contextText}";

            var request = new LlmRequest(system, user,
                ImagesBase64: images,
                MaxTokens: _settings.Current.MaxResponseTokens,
                Temperature: _settings.Current.Temperature);

            Serilog.Log.Information("LLM request: provider={Provider} model={Model} type=CaptureSession shots={N}",
                visionLlm.ProviderName, visionLlm.ModelId, images.Count);

            var responseCs = await StreamWithRetryAsync(visionLlm, request, t => _latencyTracker.RecordLlmLatency(t));
            if (_journal is not null)
                _ = JournalVisionBatchAsync(images, responseCs, "capture_session_llm");
            _overlay.SetStatus(OverlayStatus.Listening, "Listening...");
        }
        catch (LlmException ex)
        {
            _overlay.ShowError($"AI error: {ex.Message}");
            Serilog.Log.Error(ex, "LLM error during capture session send");
            _overlay.SetStatus(OverlayStatus.Listening, "Listening...");
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _overlay.ShowError("Unexpected error — check logs");
            Serilog.Log.Error(ex, "Error during capture session LLM send");
        }
    }

    /// <summary>Sends all queued session shots to the Google AI Search bridge (multi-image).</summary>
    public async Task HandleCaptureSessionGoogleAsync(IReadOnlyList<string> images)
    {
        if (images.Count == 0)
        {
            _overlay.ShowError("No shots in session — add at least one before sending.");
            return;
        }

        _overlay.SetStatus(OverlayStatus.Thinking, $"Sending {images.Count} shot(s) to Google AI Search…");
        _overlay.StartNewResponse();

        try
        {
            var payload = System.Text.Json.JsonSerializer.Serialize(new { images_base64 = images });
            using var req = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Post,
                _settings.Current.InternetSearchBaseUrl.TrimEnd('/') + "/images");
            req.Content = new System.Net.Http.StringContent(payload, System.Text.Encoding.UTF8, "application/json");
            var apiKey = _config.InternetSearchApiKey;
            if (!string.IsNullOrWhiteSpace(apiKey))
                req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
            var response = await _googleSearchHttp.SendAsync(req);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var text = doc.RootElement.GetProperty("response").GetString() ?? "No response";
            if (_journal is not null)
                _ = JournalVisionBatchAsync(images, text, "capture_session_google");

            for (int i = 0; i < text.Length; i += 8)
            {
                _overlay.AppendResponseToken(text.Substring(i, Math.Min(8, text.Length - i)));
                await Task.Delay(12);
            }
            _overlay.SetStatus(OverlayStatus.Listening, "Listening...");
        }
        catch (Exception ex)
        {
            _overlay.ShowError("Google AI Search not reachable — is start-internet-search.ps1 running?");
            Serilog.Log.Error(ex, "Google Search multi-image API error");
            _overlay.SetStatus(OverlayStatus.Listening, "Listening...");
        }
    }

    private ILlmProvider GetLlm(bool requiresVision = false) => _llm;

    public async Task RefreshCategoriesAsync(CancellationToken ct = default)
    {
        if (_rag is null) return;
        try
        {
            var categories = await _rag.GetCategoriesAsync(ct);
            if (categories.Count > 0)
            {
                _overlay.SetCategories(categories);
                Serilog.Log.Information("Discovered RAG categories: {Categories}", string.Join(", ", categories));
            }
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "Failed to fetch categories from RAG bridge");
        }
    }

    private void OnCategoryChanged(string newCategory)
    {
        Serilog.Log.Information("Category steered to '{Category}'", newCategory);
        _responseCts?.Cancel();

        if (!string.IsNullOrWhiteSpace(_lastQueryText))
        {
            _ = RequeryAsync(_lastQueryText, newCategory);
        }
    }

    private async Task RequeryAsync(string query, string category)
    {
        var activeLlm = GetLlm();
        _overlay.SetListeningState(false);
        _overlay.SetStatus(OverlayStatus.Thinking, $"Redirecting to [{category}] via {activeLlm.ModelId}…");
        _overlay.StartNewResponse();

        try
        {
            _overlay.AppendResponseToken($"[Redirected to: {category}]\nQ: {query}\n\nA: ");

            var (docBlock, hasDocs) = await GetDocContextAsync(query, category);
            var system = PromptBuilder.BuildSystemPrompt(_settings.Current.UserName,
                MeetingPlatformDetector.FriendlyName(_platformDetector.CurrentPlatform),
                _stt.DetectedLanguage,
                _templateService?.GetByName(_settings.Current.ActiveTemplateName));

            if (docBlock is not null)
                system = system + "\n\n" + docBlock;

            var modeHint = GetModeInstruction(category, hasDocs);
            if (!string.IsNullOrEmpty(modeHint))
                system = system + "\n\n" + modeHint;

            var qaTurnsPrompt = _qaHistory.BuildContextPrompt(3);
            if (!string.IsNullOrWhiteSpace(qaTurnsPrompt))
                system = system + "\n\n" + qaTurnsPrompt;

            var user = $"I'm in a live meeting. Focus on the category/domain: '{category}'. Here is my question/statement:\n\n\"{query}\"\n\n" +
                       "Answer it helpfully. Do not repeat the question back.";

            var activeTemplate = _templateService?.GetByName(_settings.Current.ActiveTemplateName);
            int maxTokens = activeTemplate?.MaxTokensOverride ?? _settings.Current.MaxResponseTokens;

            var request = new LlmRequest(system, user,
                MaxTokens: maxTokens,
                Temperature: _settings.Current.Temperature);

            Serilog.Log.Information("LLM category redirect: provider={Provider} model={Model} category={Category}",
                activeLlm.ProviderName, activeLlm.ModelId, category);

            var responseAq = await StreamWithRetryAsync(activeLlm, request, t => _latencyTracker.RecordLlmLatency(t));
            if (!string.IsNullOrWhiteSpace(responseAq))
            {
                _qaHistory.AddTurn(query, responseAq, category, _overlay.ResponseModeEnum);
            }
            if (_journal is not null)
                _ = JournalQaAsync(query, responseAq, "audio_redirect");

            _overlay.SetStatus(OverlayStatus.Listening, "Listening...");
        }
        catch (OperationCanceledException) { }
        catch (LlmException ex)
        {
            _overlay.ShowError($"AI error: {ex.Message}");
            Serilog.Log.Error(ex, "LLM error during category redirect query");
            _overlay.SetStatus(OverlayStatus.Listening, "Listening...");
        }
        catch (Exception ex)
        {
            _overlay.ShowError("Unexpected error — check logs");
            Serilog.Log.Error(ex, "Unexpected error in category redirect query");
        }
    }

    private async Task<(string? ContextBlock, bool HasDocs)> GetDocContextAsync(string query, string? category = null)
    {
        var targetCategory = string.IsNullOrWhiteSpace(category) ? _overlay.ActiveCategory : category.Trim().ToLowerInvariant();
        var url = _settings.Current.RagBridgeUrl;
        bool hasRag = _rag is not null && !string.IsNullOrWhiteSpace(url);

        if (!hasRag)
        {
            var fallback = _docContext?.BuildContextBlock();
            return (fallback, fallback is not null);
        }

        int budget = _settings.Current.MaxResponseTokens;
        IReadOnlyList<string>? catFilter = string.IsNullOrWhiteSpace(targetCategory) || targetCategory == "*" ? null : [targetCategory];
        var docsTask    = _rag!.RetrieveContextAsync(query, tokenBudget: budget * 2, categories: catFilter);
        var sessionTask = _rag!.RetrieveSessionContextAsync(query, tokenBudget: budget);

        await Task.WhenAll(docsTask, sessionTask);
        var docsCtx    = docsTask.Result;
        var sessionCtx = sessionTask.Result;
        bool hasDocs   = !string.IsNullOrWhiteSpace(docsCtx);

        if (!hasDocs && !string.IsNullOrWhiteSpace(targetCategory))
        {
            // Universal Domain Steering Fallback (US-11-07):
            docsCtx = $"Domain Focus: {targetCategory}. (Local docs: None). Answer using authoritative technical depth on {targetCategory}.";
        }
        else if (!hasDocs)
        {
            docsCtx = _docContext?.BuildContextBlock();
        }

        if (docsCtx is null && sessionCtx is null) return (null, hasDocs);
        if (sessionCtx is null) return (docsCtx, hasDocs);
        if (docsCtx is null)    return (sessionCtx, hasDocs);
        return (sessionCtx + "\n\n" + docsCtx, hasDocs);
    }

    private string GetModeInstruction(string? category = null, bool hasDocs = false)
    {
        var catName = string.IsNullOrWhiteSpace(category) ? _overlay.ActiveCategory : category.Trim();
        return _overlay.ResponseModeEnum switch
        {
            ResponseModeType.RagOnly =>
                $"Strict Grounding Mode: Your answer must be strictly derived ONLY from the provided local documents for category '{catName}'. If the information is not present in the provided local documents, respond with: \"Information not found in local documents.\"",

            ResponseModeType.Detailed =>
                hasDocs
                    ? $"Format your response into exactly two distinct sections:\n\n" +
                      $"━━━ FROM LOCAL DOCS ━━━━━━━━━━━━━━━━━━━━━━━━\n" +
                      $"Summarize specific project architecture, metrics, responsibilities, and facts directly from the provided local documents.\n\n" +
                      $"━━━ EXTENDED ARCHITECTURE & BEST PRACTICES ━\n" +
                      $"Elaborate with industry best practices, architectural trade-offs, scalability considerations, and theoretical depth relevant to the question and category '{catName}'."
                    : $"Format your response into exactly two distinct sections:\n\n" +
                      $"━━━ FROM LOCAL DOCS ━━━━━━━━━━━━━━━━━━━━━━━━\n" +
                      $"No local documents found for category '{catName}'.\n\n" +
                      $"━━━ EXTENDED ARCHITECTURE & BEST PRACTICES ━\n" +
                      $"Provide complete, authoritative theoretical coverage, best practices, and architectural depth on the question for domain '{catName}'.",

            ResponseModeType.Coding =>
                "Respond with code examples. Prefer working, idiomatic code over prose. Briefly explain key design decisions, complexity, and edge cases.",

            _ => "Respond directly and conversationally in 2-3 clear, informative paragraphs."
        };
    }

    /// <summary>
    /// Activates or deactivates low-power mode. In low-power mode, the periodic GC
    /// interval doubles (less CPU overhead from GC). The overlay status indicates the
    /// mode to the user. Model-level throttling requires an app restart to take effect
    /// (Whisper model is loaded at startup; AppSettings.WhisperModel is saved on exit).
    /// </summary>
    public void SetLowPowerMode(bool active)
    {
        if (active)
        {
            // Slow down the GC timer to reduce CPU overhead (already running at 10 min;
            // change to 20 min in low-power mode)
            _gcTimer?.Change(TimeSpan.FromMinutes(20), TimeSpan.FromMinutes(20));
            Serilog.Log.Information("Low-power mode activated — GC interval extended to 20 min");
        }
        else
        {
            _gcTimer?.Change(TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(10));
            Serilog.Log.Information("Low-power mode deactivated — GC interval restored to 10 min");
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

    private async Task HandleAudioQueryAsync(float[]? recordedSamples, DateTimeOffset holdEnd, bool useRag = true)
    {
        var ct = _cts?.Token ?? CancellationToken.None;
        var activeLlm = GetLlm();
        _overlay.SetListeningState(false);
        _overlay.SetStatus(OverlayStatus.Thinking, $"Asking {activeLlm.ModelId}…");
        _overlay.StartNewResponse();

        try
        {
            if (recordedSamples is not { Length: > 1600 }) // need ≥ 0.1s at 16 kHz
            {
                _overlay.ShowError("No audio captured — press the hotkey, speak, then press again to send.");
                _overlay.SetStatus(OverlayStatus.Listening, "Listening...");
                return;
            }

            double secs = recordedSamples.Length / 16000.0;
            var mode = _settings.Current.QueryMode;
            string? audioBase64 = null;
            string? localTranscript = null;

            bool useAudioToLlm = mode == QueryMode.AudioToLlm && _llm.SupportsAudioInput;
            if (mode == QueryMode.AudioToLlm && !_llm.SupportsAudioInput)
            {
                // Provider doesn't accept audio — fall back to local transcription silently.
                // (Only gpt-4o-audio-preview supports input_audio; NVIDIA NIM and others silently drop it.)
                _overlay.SetStatus(OverlayStatus.Thinking,
                    $"Audio-to-LLM not supported by {_llm.ModelId} — transcribing locally…");
                Serilog.Log.Warning("AudioToLlm requested but {Model} does not support audio input — falling back to local STT", _llm.ModelId);
            }

            if (useAudioToLlm)
            {
                // Send the recorded audio straight to a multimodal LLM that accepts audio input.
                var wavBytes = WavEncoder.ToWavBytes(recordedSamples);
                audioBase64 = Convert.ToBase64String(wavBytes);
                Array.Clear(recordedSamples);
                Serilog.Log.Information("Audio-to-LLM: {Secs:F1}s → {KB}KB WAV", secs, wavBytes.Length / 1024);
            }
            else
            {
                // Transcribe locally and send text — covers LocalTranscribeToLlm and AudioToLlm fallback.
                _overlay.SetStatus(OverlayStatus.Thinking, "Transcribing locally…");
                localTranscript = await _stt.TranscribeAsync(recordedSamples, AudioSource.Microphone, ct);
                Array.Clear(recordedSamples);
                Serilog.Log.Information("Local STT ({Secs:F1}s) → '{Text}'", secs, localTranscript);
                if (string.IsNullOrWhiteSpace(localTranscript) || IsWhisperHallucination(localTranscript))
                {
                    _overlay.ShowError("Didn't catch any speech in the recording — try again.");
                    _overlay.SetStatus(OverlayStatus.Listening, "Listening...");
                    return;
                }
            }

            var activeCategory = _overlay.ActiveCategory;
            _lastQueryText = localTranscript;
            _lastQueryHoldEnd = holdEnd;
            _lastQueryUsedRag = useRag;

            // Only attach the rolling meeting transcript if the user explicitly opted in.
            var contextText = _settings.Current.IncludeTranscriptContext
                ? _context.BuildContext(holdEnd, _settings.Current.MaxResponseTokens * 2)
                : string.Empty;

            var ragQuery = localTranscript ?? contextText ?? "meeting context";
            // Enrich retrieval query with previous question topic if follow-up is terse
            var recentTurns = _qaHistory.GetRecentTurns(1);
            if (recentTurns.Count > 0 && ragQuery.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length <= 8)
            {
                ragQuery = $"{ragQuery} (Context: {recentTurns[0].Question})";
            }

            var (docBlock, hasDocs) = useRag ? await GetDocContextAsync(ragQuery, activeCategory) : (null, false);

            if (useRag && !hasDocs && !string.IsNullOrWhiteSpace(activeCategory))
            {
                _overlay.SetStatus(OverlayStatus.Thinking, $"Asking {activeLlm.ModelId} ({activeCategory} - Domain mode)…");
            }

            var system = PromptBuilder.BuildSystemPrompt(_settings.Current.UserName,
                MeetingPlatformDetector.FriendlyName(_platformDetector.CurrentPlatform),
                _stt.DetectedLanguage,
                _templateService?.GetByName(_settings.Current.ActiveTemplateName));
            if (docBlock is not null)
                system = system + "\n\n" + docBlock;
            var modeHint = GetModeInstruction(activeCategory, hasDocs);
            if (!string.IsNullOrEmpty(modeHint))
                system = system + "\n\n" + modeHint;

            var qaTurnsPrompt = _qaHistory.BuildContextPrompt(3);
            if (!string.IsNullOrWhiteSpace(qaTurnsPrompt))
                system = system + "\n\n" + qaTurnsPrompt;

            string user;
            if (audioBase64 is not null)
            {
                user = "Listen to the attached audio from a live meeting. " +
                       "Start your response with 'Q: ' followed by a one-line summary of the key question or statement you heard. " +
                       "Then on a new line start with 'A: ' and give a helpful answer.";
                if (!string.IsNullOrWhiteSpace(contextText))
                    user += $"\n\nAdditional meeting transcript context:\n{contextText}";
            }
            else
            {
                // Local path: show the recognised question, then ask the model to answer it.
                _overlay.AppendResponseToken($"Q: {localTranscript}\n\nA: ");
                user = $"I'm in a live meeting. Domain focus: '{activeCategory}'. Here is my question/statement, transcribed from audio:\n\n\"{localTranscript}\"\n\n" +
                       "Answer it helpfully. Do not repeat the question back.";
                if (!string.IsNullOrWhiteSpace(contextText))
                    user += $"\n\nMeeting context:\n{contextText}";
            }

            var activeTemplate = _templateService?.GetByName(_settings.Current.ActiveTemplateName);
            int maxTokens = activeTemplate?.MaxTokensOverride ?? _settings.Current.MaxResponseTokens;

            var request = new LlmRequest(system, user,
                AudioBase64: audioBase64,
                AudioMediaType: audioBase64 is not null ? "audio/wav" : null,
                MaxTokens: maxTokens,
                Temperature: _settings.Current.Temperature);

            Serilog.Log.Information("LLM request: provider={Provider} model={Model} mode={Mode} category={Category}",
                activeLlm.ProviderName, activeLlm.ModelId,
                audioBase64 is not null ? "AudioToLlm" : "LocalTranscribeToLlm",
                activeCategory);

            var responseAq = await StreamWithRetryAsync(activeLlm, request, t => _latencyTracker.RecordLlmLatency(t));
            if (!string.IsNullOrWhiteSpace(responseAq))
            {
                _qaHistory.AddTurn(localTranscript ?? "(audio question)", responseAq, activeCategory, _overlay.ResponseModeEnum);
            }
            if (_journal is not null)
                _ = JournalQaAsync(localTranscript ?? contextText ?? "(audio question)", responseAq, "audio");

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
        var activeLlm = GetLlm();
        _overlay.SetStatus(OverlayStatus.Thinking, $"Extracting action items via {activeLlm.ModelId}…");
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
            var (docBlock2, hasDocs2) = await GetDocContextAsync("action items decisions owners");
            if (docBlock2 is not null)
                system = system + "\n\n" + docBlock2;
            var modeHint2 = GetModeInstruction(_overlay.ActiveCategory, hasDocs2);
            if (!string.IsNullOrEmpty(modeHint2))
                system = system + "\n\n" + modeHint2;
            var user = $"Extract all action items, decisions, and owners from this meeting transcript. Format as a bulleted list with owner names where identifiable.\n\n{sb}";
            var request = new LlmRequest(system, user, MaxTokens: 1024);

            Serilog.Log.Information("LLM request: provider={Provider} model={Model} type=ActionItems",
                activeLlm.ProviderName, activeLlm.ModelId);

            var responseAi = await StreamWithRetryAsync(activeLlm, request, t => _latencyTracker.RecordLlmLatency(t));
            if (_journal is not null)
                _ = JournalQaAsync("action items and decisions", responseAi, "action_items");

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

        var visionLlm = GetLlm(requiresVision: true);
        _overlay.SetStatus(OverlayStatus.Thinking, $"Analysing image via {visionLlm.ModelId}…");

        try
        {
            var contextText = _context.BuildContext(DateTimeOffset.UtcNow, _settings.Current.MaxResponseTokens);
            var system = PromptBuilder.BuildSystemPrompt(_settings.Current.UserName,
                MeetingPlatformDetector.FriendlyName(_platformDetector.CurrentPlatform),
                _stt.DetectedLanguage,
                _templateService?.GetByName(_settings.Current.ActiveTemplateName));
            var (docBlock3, hasDocs3) = await GetDocContextAsync(
                contextText.Length > 300 ? contextText[..300] : (contextText.Length > 0 ? contextText : "image analysis"));
            if (docBlock3 is not null)
                system = system + "\n\n" + docBlock3;
            var modeHint3 = GetModeInstruction(_overlay.ActiveCategory, hasDocs3);
            if (!string.IsNullOrEmpty(modeHint3))
                system = system + "\n\n" + modeHint3;
            var user = PromptBuilder.BuildUserMessage(contextText, hasImage: true);
            var request = new LlmRequest(system, user,
                ImageBase64: imageBase64,
                ImageMediaType: "image/jpeg",
                MaxTokens: _settings.Current.MaxResponseTokens);

            Serilog.Log.Information("LLM request: provider={Provider} model={Model} type=ImageDrop",
                visionLlm.ProviderName, visionLlm.ModelId);

            var responseDi = await StreamWithRetryAsync(visionLlm, request, t => _latencyTracker.RecordLlmLatency(t));
            if (_journal is not null)
                _ = JournalVisionAsync(imageBase64, responseDi, "dropped_image");

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

        var visionLlm2 = GetLlm(requiresVision: true);
        _overlay.SetStatus(OverlayStatus.Thinking, $"Analysing screen via {visionLlm2.ModelId}…");

        try
        {
            var contextText = _context.BuildContext(DateTimeOffset.UtcNow, _settings.Current.MaxResponseTokens);
            var system = PromptBuilder.BuildSystemPrompt(_settings.Current.UserName,
                MeetingPlatformDetector.FriendlyName(_platformDetector.CurrentPlatform),
                _stt.DetectedLanguage,
                _templateService?.GetByName(_settings.Current.ActiveTemplateName));
            var (docBlock4, hasDocs4) = await GetDocContextAsync(
                contextText.Length > 300 ? contextText[..300] : (contextText.Length > 0 ? contextText : "screen capture analysis"));
            if (docBlock4 is not null)
                system = system + "\n\n" + docBlock4;
            var modeHint4 = GetModeInstruction(_overlay.ActiveCategory, hasDocs4);
            if (!string.IsNullOrEmpty(modeHint4))
                system = system + "\n\n" + modeHint4;
            var user = PromptBuilder.BuildUserMessage(contextText, hasImage: true);
            var request = new LlmRequest(system, user,
                ImageBase64: imageBase64,
                ImageMediaType: "image/jpeg",
                MaxTokens: _settings.Current.MaxResponseTokens);

            Serilog.Log.Information("LLM request: provider={Provider} model={Model} type=ScreenCapture",
                visionLlm2.ProviderName, visionLlm2.ModelId);

            var responseSc = await StreamWithRetryAsync(visionLlm2, request, t => _latencyTracker.RecordLlmLatency(t));
            if (_journal is not null)
                _ = JournalVisionAsync(imageBase64, responseSc, "screen_capture");

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

    /// <summary>
    /// Called by App after the snip overlay returns a user-selected region.
    /// Captures just that region from DXGI and sends it to the LLM.
    /// </summary>
    public async Task HandleSnipCaptureAsync(System.Drawing.Rectangle region)
    {
        _overlay.SetStatus(OverlayStatus.Thinking, "Capturing selected region…");
        _overlay.StartNewResponse();

        // Hide the overlay HWND before capturing. WDA_EXCLUDEFROMCAPTURE (applied when
        // ExcludeFromScreenCapture is on) makes our window appear solid black in DXGI/GDI
        // captures even when the content is transparent. Hiding the HWND removes the window
        // from DWM composition entirely so the content below is captured cleanly.
        _overlay.HideForCapture();
        string? imageBase64 = null;
        try
        {
            await Task.Delay(200); // allow DWM to recomposite without the overlay
            // GDI capture: confirmed reliable on this system. DXGI was returning solid-black
            // frames on the hybrid GPU (discrete GPU has no primary display output). GDI reads
            // from DWM's composited buffer and captures all windows correctly.
            imageBase64 = await Task.Run(() =>
                DxgiScreenCapture.CaptureRegionViaGdi(region, maxWidthPx: 1280, jpegQuality: 85));
            Serilog.Log.Information(
                "Snip GDI capture: region={Region} imageNull={Null} imageLen={Len}",
                region, imageBase64 is null, imageBase64?.Length ?? 0);
        }
        catch (Exception ex)
        {
            _overlay.ShowError("Region capture failed — check logs");
            Serilog.Log.Error(ex, "Snip region capture exception");
            _overlay.SetStatus(OverlayStatus.Listening, "Listening...");
            return;
        }
        finally
        {
            _overlay.RestoreAfterCapture();
        }

        if (string.IsNullOrEmpty(imageBase64))
        {
            _overlay.ShowError("Could not capture selected region — region may be outside primary display.");
            _overlay.SetStatus(OverlayStatus.Listening, "Listening...");
            return;
        }

        var visionLlm3 = GetLlm(requiresVision: true);
        _overlay.SetStatus(OverlayStatus.Thinking, $"Analysing region via {visionLlm3.ModelId}…");

        try
        {
            var contextText = _context.BuildContext(DateTimeOffset.UtcNow, _settings.Current.MaxResponseTokens);
            var system = PromptBuilder.BuildSystemPrompt(_settings.Current.UserName,
                MeetingPlatformDetector.FriendlyName(_platformDetector.CurrentPlatform),
                _stt.DetectedLanguage,
                _templateService?.GetByName(_settings.Current.ActiveTemplateName));
            // Apply response-mode style (Quick / Detailed / Code / etc.) to the snip answer
            var snipModeHint = GetModeInstruction();
            if (!string.IsNullOrEmpty(snipModeHint))
                system = system + "\n\n" + snipModeHint;
            var user = PromptBuilder.BuildUserMessage(contextText, hasImage: true, forSnipCapture: true);
            var request = new LlmRequest(system, user,
                ImageBase64: imageBase64,
                ImageMediaType: "image/jpeg",
                MaxTokens: _settings.Current.MaxResponseTokens);

            Serilog.Log.Information("LLM request: provider={Provider} model={Model} type=SnipCapture region={Region} mode={Mode}",
                visionLlm3.ProviderName, visionLlm3.ModelId, region, _overlay.ResponseMode);

            var responseSnip = await StreamWithRetryAsync(visionLlm3, request, t => _latencyTracker.RecordLlmLatency(t));
            if (_journal is not null)
                _ = JournalVisionAsync(imageBase64, responseSnip, "snip_capture");
            _overlay.SetStatus(OverlayStatus.Listening, "Listening...");
        }
        catch (LlmException ex)
        {
            _overlay.ShowError($"AI error: {ex.Message}");
            Serilog.Log.Error(ex, "LLM error during snip capture query");
            _overlay.SetStatus(OverlayStatus.Listening, "Listening...");
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _overlay.ShowError("Unexpected error — check logs");
            Serilog.Log.Error(ex, "Unexpected error in snip capture query");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _cts?.Cancel();
        _cts?.Dispose();
        _responseCts?.Cancel();
        _responseCts?.Dispose();
        _captureConfirmTimer?.Dispose();
        _gcTimer?.Dispose();
        _latencyLogTimer?.Dispose();
        _transcriptJournalTimer?.Dispose();
        _journal?.Dispose();
        _audio.Dispose();
        _stt.Dispose();
        _hotkeys.Dispose();
        _shareDetector.Dispose();
        _platformDetector.Dispose();
        _captionsReader.Dispose();
    }

    // ── Session memory helpers ─────────────────────────────────────────────────

    private async Task JournalQaAsync(string question, string answer, string queryType)
    {
        if (_journal is null) return;
        _journal.WriteQaEvent(question, answer, queryType);
        var id = $"qa_{DateTimeOffset.UtcNow:yyyyMMddHHmmss}_{queryType}";
        var title = $"Q&A [{DateTimeOffset.Now:HH:mm}] ({queryType})";
        var text = $"Q: {question}\nA: {answer}";
        if (_rag is not null)
            await _rag.IndexSessionDocumentAsync(id, title, text);
    }

    private async Task JournalVisionAsync(string imageBase64, string response, string queryType)
    {
        if (_journal is null) return;
        var imagePath = _journal.SaveImage(imageBase64);
        var caption = await GenerateVisionCaptionAsync(imageBase64);
        _journal.WriteVisionEvent(imagePath, caption, response, queryType);
        var id = $"vision_{DateTimeOffset.UtcNow:yyyyMMddHHmmss}_{queryType}";
        var title = $"Vision [{DateTimeOffset.Now:HH:mm}] ({queryType})";
        var text = $"[Visual content: {caption}]\n\nResponse: {response}";
        if (_rag is not null)
            await _rag.IndexSessionDocumentAsync(id, title, text);
    }

    private async Task JournalVisionBatchAsync(IReadOnlyList<string> images, string response, string queryType)
    {
        if (_journal is null) return;
        // Index first image for retrieval; additional frames are context only
        var primaryImage = images.Count > 0 ? images[0] : null;
        if (primaryImage is not null)
            await JournalVisionAsync(primaryImage, response, queryType);
        else
        {
            var id = $"vision_{DateTimeOffset.UtcNow:yyyyMMddHHmmss}_{queryType}";
            if (_rag is not null)
                await _rag.IndexSessionDocumentAsync(id,
                    $"Vision [{DateTimeOffset.Now:HH:mm}] ({queryType})",
                    $"Response: {response}");
        }
    }

    private async Task<string> GenerateVisionCaptionAsync(string imageBase64)
    {
        try
        {
            var llm = GetLlm(requiresVision: true);
            var captionRequest = new LlmRequest(
                SystemPrompt: "You are a precise image describer. Describe only what is literally visible.",
                UserMessage: "Describe this image in 2-3 sentences. Focus on specific visible details: text, numbers, UI elements, error messages, graphs, code. Be precise enough that someone who cannot see the image could understand the context.",
                ImageBase64: imageBase64,
                ImageMediaType: "image/jpeg",
                MaxTokens: 150,
                Temperature: 0.1f);
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var sb = new System.Text.StringBuilder();
            await foreach (var token in llm.StreamResponseAsync(captionRequest, cts.Token))
                sb.Append(token);
            return sb.ToString();
        }
        catch (Exception ex)
        {
            Serilog.Log.Debug(ex, "Visual caption generation failed — using placeholder");
            return "(visual content)";
        }
    }

    private async Task JournalTranscriptTickAsync()
    {
        if (_journal is null || _rag is null) return;
        await IndexTranscriptChunkAsync();
    }

    private async Task IndexTranscriptChunkAsync()
    {
        if (_journal is null) return;
        var cutoff = _lastTranscriptChunkTime;
        var since  = _transcript.GetSince(cutoff);
        if (since.Count == 0) return;

        var chunkStart = since[0].Timestamp;
        var chunkEnd   = since[since.Count - 1].Timestamp;
        _lastTranscriptChunkTime = chunkEnd;

        var sb = new System.Text.StringBuilder();
        foreach (var seg in since)
            sb.AppendLine($"[{seg.Timestamp:HH:mm:ss}] {seg.SpeakerLabel}: {seg.Text}");

        var id    = $"transcript_{chunkStart:yyyyMMddHHmmss}";
        var title = $"Transcript [{chunkStart:HH:mm}–{chunkEnd:HH:mm}]";
        if (_rag is not null)
            await _rag.IndexSessionDocumentAsync(id, title, sb.ToString());
    }
}

using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Forms;
using Chum.App.Models;
using Chum.App.Services;
using Chum.App.ViewModels;
using Chum.App.Views;
using Chum.Audio.Capture;
using Chum.Audio.Pipeline;
using Chum.Audio.Vad;
using Chum.Llm;
using Chum.Transcription;
using Serilog;

namespace Chum.App;

public partial class App : System.Windows.Application
{
    // Publicly accessible services (used by SettingsWindow)
    public SettingsService Settings { get; } = new();
    public ConfigFileService Config { get; } = new();
    public DocumentContextService DocContext { get; } = new();
    public MeetingOrchestrator? Orchestrator => _orchestrator;

    private HotkeyService? _hotkeys;
    private MeetingOrchestrator? _orchestrator;
    private OverlayWindow? _overlayWindow;
    private OverlayViewModel? _overlayVm;
    private NotifyIcon? _trayIcon;
    private DxgiScreenCapture? _screenCapture;
    private ClipboardMonitor? _clipboardMonitor;
    private RegionSelectionService? _regionSelection;
    private bool _started;
    private bool _googleSearchMode;
    private bool _captureSessionMode;
    private string? _pendingMeetingDeviceId;
    private Views.RegionBorderWindow? _regionBorderWindow;
    private System.Drawing.Rectangle _sessionRegion;

    // Auto-updater
    private readonly HttpClient   _updateHttp    = new();
    private UpdateChecker?        _updateChecker;
    private UpdateInfo?           _pendingUpdate;
    private ToolStripMenuItem?    _updateMenuItem;

    protected override async void OnStartup(StartupEventArgs e)
    {
        var startupSw = Stopwatch.StartNew();
        base.OnStartup(e);

        ConfigureLogging();
        Log.Information("Chum starting up");

        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        Settings.Load();
        Config.Load();
        DocContext.Load();

        // Create overlay early so we can show startup status immediately
        _overlayVm = new OverlayViewModel(Dispatcher);
        _overlayWindow = new OverlayWindow(_overlayVm);

        bool localMode = Settings.Current.LocalOnlyMode;
        bool hasKey = localMode
                   || Config.AnthropicApiKey is not null
                   || Config.OpenAiApiKey is not null;
        if (!hasKey)
        {
            Log.Information("No API key found — showing settings on first run");
            var setup = new SettingsWindow();
            setup.ShowDialog();
            localMode = Settings.Current.LocalOnlyMode;
            hasKey = localMode
                  || Config.AnthropicApiKey is not null
                  || Config.OpenAiApiKey is not null;
            if (!hasKey)
            {
                Log.Warning("No API key provided — exiting");
                Shutdown();
                return;
            }
        }

        // Show overlay before component build so the user sees the app immediately
        _overlayWindow.Show();
        _overlayVm.SetStatus(OverlayStatus.Initialising, "Starting up…");

        await BuildAndWireComponentsAsync();
        ApplyCaptureExclusionToOverlay();
        CreateTrayIcon();

        _updateChecker = new UpdateChecker(_updateHttp);
        _ = RunUpdateCheckAsync(CancellationToken.None);

        startupSw.Stop();
        var startupMs = startupSw.Elapsed.TotalMilliseconds;
        Log.Information("Startup complete in {Ms:F0} ms (target ≤3000 ms)", startupMs);
        if (startupMs > 3000)
            Log.Warning("Startup exceeded 3 s target by {Extra:F0} ms", startupMs - 3000);

        _overlayVm.SetStatus(OverlayStatus.Idle, "Ready");

        if (Settings.Current.StartCapturingOnLaunch)
            await StartCaptureAsync();
    }

    private async Task BuildAndWireComponentsAsync()
    {
        // Run the two VAD ONNX session loads and template file I/O in parallel — each can take
        // hundreds of ms on cold storage; overlapping them cuts perceived startup time.
        var vadTask1 = Task.Run(BuildVad);
        var vadTask2 = Task.Run(BuildVad);
        var templates = new TemplateService();
        var templatesTask = Task.Run(templates.Load);

        ILlmProvider llm = BuildLlmProvider();

        // HotkeyService.Install must run on the STA UI thread with a live message loop — do it here
        // (before any awaits that would suspend) so the constraint is always satisfied.
        _hotkeys = new HotkeyService();
        RegisterHotkeys();
        _hotkeys.Install();

        DxgiScreenCapture.TryCreate(out _screenCapture);
        _clipboardMonitor = new ClipboardMonitor();

        await Task.WhenAll(vadTask1, vadTask2, templatesTask);

        var loopback = new LoopbackCapture(Settings.Current.LoopbackDeviceId);
        var mic = new MicCapture(Settings.Current.MicDeviceId);
        var audioPipeline = new AudioPipeline(loopback, mic, vadTask1.Result, vadTask2.Result,
            enableNoiseSuppression: Settings.Current.EnableNoiseSuppression);

        var sttBaseUrl = string.IsNullOrWhiteSpace(Settings.Current.CloudSttBaseUrl)
            ? null : Settings.Current.CloudSttBaseUrl;
        ISttEngine stt = new OpenAiSttProvider(
            Config.CloudSttApiKey ?? "", Settings.Current.CloudSttModel, sttBaseUrl);

        var retentionWindow = TimeSpan.FromMinutes(Settings.Current.TranscriptRetentionMinutes);
        var transcriptBuffer = new TranscriptBuffer(retentionWindow);
        var contextExtractor = new ContextExtractor(transcriptBuffer);

        _orchestrator = new MeetingOrchestrator(
            audioPipeline, stt, transcriptBuffer, contextExtractor,
            llm, _hotkeys, _overlayVm!, Settings, _screenCapture, _clipboardMonitor, templates, DocContext);

        _overlayWindow!.ImageFileDropped += (_, path) =>
            _ = _orchestrator.HandleDroppedImageQueryAsync(path);

        _orchestrator.DeviceDisconnected += async (_, _) =>
            await FallbackToDefaultAudioAsync();

        _orchestrator.MeetingAppOpened += async (_, _) =>
        {
            if (!Settings.Current.AutoStartCapture || _orchestrator.IsRunning) return;
            await _orchestrator.StartAsync();
        };
        _orchestrator.MeetingAppClosed += async (_, _) =>
        {
            if (!Settings.Current.AutoStartCapture || !_orchestrator.IsRunning) return;
            await _orchestrator.StopAsync();
        };

        _orchestrator.AudioDeviceMismatchDetected += (_, e) =>
        {
            _pendingMeetingDeviceId = e.DeviceId;
            _overlayVm?.ShowAudioDeviceMismatch(
                $"⚠ {e.PlatformName} audio is on '{e.DeviceName}'. Switch Chum capture to match?");
        };

        _orchestrator.SnipModeRequested += (_, _) =>
        {
            _ = Dispatcher.InvokeAsync(async () =>
            {
                var snip = new SnipOverlayWindow();
                var region = await snip.ShowAndGetSelectionAsync();
                if (region is not null && _orchestrator is not null)
                    await _orchestrator.HandleSnipCaptureAsync(region.Value);
            });
        };

        // Screenshot mode: two double-clicks on screen define opposite corners of the
        // region to send to the vision model. Reuses the orchestrator's region-capture path.
        _regionSelection = new RegionSelectionService(Dispatcher);
        _regionSelection.FirstPointCaptured += (_, _) =>
        {
            var hint = _captureSessionMode
                ? "Session: first corner set — double-click the opposite corner"
                : "First corner set — double-click the opposite corner to capture";
            _overlayVm?.SetScreenshotMode(true, hint);
        };
        _regionSelection.RegionSelected += (_, region) =>
        {
            _overlayVm?.SetScreenshotMode(false, string.Empty);
            _overlayVm?.SetGoogleSearchMode(false);
            if (region.Width < 5 || region.Height < 5)
            {
                _overlayVm?.ShowError("Selected region is too small — try two corners further apart.");
                _googleSearchMode = false;
                _captureSessionMode = false;
                return;
            }
            if (_orchestrator is not null)
            {
                if (_captureSessionMode)
                {
                    _captureSessionMode = false;
                    _sessionRegion = region;
                    _overlayVm?.BeginCaptureSession();
                    _regionBorderWindow?.Close();
                    _regionBorderWindow = new Views.RegionBorderWindow();
                    _regionBorderWindow.ShowOverRegion(_sessionRegion);
                    _ = AddCaptureSessionShotAsync();
                }
                else if (_googleSearchMode)
                {
                    _googleSearchMode = false;
                    _ = _orchestrator.HandleGoogleSearchAsync(region);
                }
                else
                {
                    _ = _orchestrator.HandleSnipCaptureAsync(region);
                }
            }
        };

        _overlayWindow.Opacity = Settings.Current.OverlayOpacity;
        Settings.SettingsChanged += (_, _) =>
            Dispatcher.InvokeAsync(() => _overlayWindow.Opacity = Settings.Current.OverlayOpacity);
    }

    public void ReapplyHotkeys()
    {
        if (_hotkeys is null) return;
        RegisterHotkeys();
    }

    public void ApplyCaptureExclusionToOverlay()
    {
        _overlayWindow?.SetCaptureExclusion(Settings.Current.ExcludeFromScreenCapture);
    }

    public async Task ApplyAudioDevicesAsync()
    {
        if (_orchestrator is null) return;
        var wasStarted = _started;
        if (wasStarted)
        {
            await _orchestrator.StopAsync();
            _started = false;
        }

        var s = Settings.Current;
        var newLoopback = new LoopbackCapture(s.LoopbackDeviceId);
        var newMic = new MicCapture(s.MicDeviceId);
        var newPipeline = new AudioPipeline(newLoopback, newMic, BuildVad(), BuildVad(),
            enableNoiseSuppression: s.EnableNoiseSuppression);
        _orchestrator.ReplaceAudio(newPipeline);

        if (wasStarted)
            await StartCaptureAsync();
    }

    private async Task FallbackToDefaultAudioAsync()
    {
        if (_orchestrator is null) return;
        Log.Warning("Audio device failover triggered — rebuilding pipeline to Windows default");

        var wasStarted = _started;
        if (wasStarted)
        {
            await _orchestrator.StopAsync();
            _started = false;
        }

        // Rebuild with null device IDs = Windows default, without changing saved settings
        var newPipeline = new AudioPipeline(new LoopbackCapture(null), new MicCapture(null), BuildVad(), BuildVad(),
            enableNoiseSuppression: Settings.Current.EnableNoiseSuppression);
        _orchestrator.ReplaceAudio(newPipeline);

        if (wasStarted)
            await StartCaptureAsync();
    }

    private void RegisterHotkeys()
    {
        var s = Settings.Current;
        _hotkeys!.RegisterFromString("HoldToAsk", s.HoldToAskHotkey);
        _hotkeys.RegisterFromString("ScreenCapture", s.ScreenCaptureHotkey);
        _hotkeys.RegisterFromString("PrivacyPause", s.PrivacyPauseHotkey);
        _hotkeys.RegisterFromString("HideOverlay", s.HideOverlayHotkey);
        _hotkeys.RegisterFromString("ActionItems", s.ActionItemsHotkey);

        // Template selection: Ctrl+Alt+1..5 (fixed, not user-configurable)
        var templateMods = System.Windows.Input.ModifierKeys.Control | System.Windows.Input.ModifierKeys.Alt;
        _hotkeys.Register("Template1", System.Windows.Input.Key.D1, templateMods);
        _hotkeys.Register("Template2", System.Windows.Input.Key.D2, templateMods);
        _hotkeys.Register("Template3", System.Windows.Input.Key.D3, templateMods);
        _hotkeys.Register("Template4", System.Windows.Input.Key.D4, templateMods);
        _hotkeys.Register("Template5", System.Windows.Input.Key.D5, templateMods);
    }

    private IVad BuildVad()
    {
        // EnergyVad is pure DSP — no ONNX runtime conflicts. Noise suppression + a configurable
        // threshold handle noisy rooms instead of Silero.
        var s = Settings.Current;
        return new EnergyVad(onThresholdDb: s.VadThresholdDb, offThresholdDb: s.VadThresholdDb - 5f);
    }

    private ILlmProvider BuildLlmProvider()
    {
        var s = Settings.Current;

        if (s.LocalOnlyMode || s.LlmProvider == "Ollama")
        {
            Log.Information("Local-only mode — using Ollama: {Model} at {Url}", s.OllamaModel, s.OllamaBaseUrl);
            return new OllamaLlmProvider(s.OllamaModel, s.OllamaBaseUrl);
        }

        if (s.LlmProvider == "Anthropic")
        {
            var key = Config.AnthropicApiKey
                ?? throw new InvalidOperationException("Anthropic API key not set — open Settings to add it");
            Log.Information("Using Anthropic provider: {Model}", s.LlmModel);
            return new AnthropicLlmProvider(key, s.LlmModel);
        }

        // OpenAI / NVIDIA / Custom — all OpenAI-compatible
        {
            var key = Config.OpenAiApiKey
                ?? throw new InvalidOperationException("API key not set — open Settings to add it");
            var baseUrl = string.IsNullOrWhiteSpace(s.LlmApiBaseUrl) ? null : s.LlmApiBaseUrl;
            Log.Information("Using OpenAI-compatible provider ({Provider}): {Model} at {Url}",
                s.LlmProvider, s.LlmModel, baseUrl ?? "https://api.openai.com/v1");
            return new OpenAiLlmProvider(key, s.LlmModel, baseUrl);
        }
    }

    private async Task StartCaptureAsync()
    {
        if (_started || _orchestrator is null) return;
        _started = true;
        await _orchestrator.StartAsync();
    }

    private void CreateTrayIcon()
    {
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "chum.ico");
        _trayIcon = new NotifyIcon
        {
            Icon = File.Exists(iconPath)
                ? new System.Drawing.Icon(iconPath)
                : System.Drawing.SystemIcons.Application,
            Text = "",
            Visible = true
        };

        var menu = new ContextMenuStrip();
        menu.Items.Add("Show Overlay", null, (_, _) => ShowOverlay());
        menu.Items.Add("Settings", null, (_, _) => OpenSettings());
        menu.Items.Add("Export Transcript…", null, (_, _) => ExportTranscript());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Start Capture", null, async (_, _) => await StartCaptureAsync());
        menu.Items.Add("Stop Capture", null, async (_, _) =>
        {
            if (_orchestrator is not null) await _orchestrator.StopAsync();
            _started = false;
        });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("About…", null, (_, _) => new Views.AboutWindow().ShowDialog());
        menu.Items.Add("Check for Updates", null, async (_, _) => await CheckForUpdatesManuallyAsync());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Quit Chum", null, (_, _) => Shutdown());

        _trayIcon.ContextMenuStrip = menu;
        _trayIcon.DoubleClick     += (_, _) => ShowOverlay();
        _trayIcon.BalloonTipClicked += async (_, _) =>
        {
            if (_pendingUpdate is not null) await ApplyUpdateAsync();
        };
    }

    private void ShowOverlay()
    {
        _overlayWindow?.Show();
        _overlayVm?.Show();
    }

    private void OpenSettings()
    {
        var win = new SettingsWindow();
        win.ShowDialog();
    }

    public void SwitchToTeamsAudioDevice()
    {
        if (_pendingMeetingDeviceId is null || _orchestrator is null) return;
        var deviceId = _pendingMeetingDeviceId;
        _pendingMeetingDeviceId = null;
        _overlayVm?.DismissAudioDeviceMismatch();
        Settings.Update(s => s.LoopbackDeviceId = deviceId);
        Log.Information("Switching loopback capture to meeting app audio device: {Id}", deviceId);
        _ = ApplyAudioDevicesAsync();
    }

    public void DismissAudioDeviceMismatch()
    {
        _pendingMeetingDeviceId = null;
        _overlayVm?.DismissAudioDeviceMismatch();
    }

    /// <summary>Toolbar button: start/stop recording a spoken question (parallels the hotkey).</summary>
    public void ToggleRecording() => _orchestrator?.ToggleRecording();

    /// <summary>Toolbar button: stop the in-progress LLM response.</summary>
    public void StopResponse() => _orchestrator?.CancelResponse();

    /// <summary>Toolbar button: cycle response mode 1→2→3→4→5→1.</summary>
    public void CycleResponseMode() => _overlayVm?.CycleResponseMode();


    /// <summary>
    /// Toolbar button: toggle screenshot mode. Entering arms the double-click region picker;
    /// clicking again before the second double-click cancels it.
    /// </summary>
    public void ToggleScreenshotMode()
    {
        // If a capture session is active, ▣ sends the batch via the configured LLM
        if (TrySendCaptureSessionViaLlm()) return;

        if (_regionSelection is null) return;
        if (_regionSelection.IsArmed)
        {
            _regionSelection.Disarm();
            _overlayVm?.SetScreenshotMode(false, string.Empty);
        }
        else
        {
            _regionSelection.Arm();
            _overlayVm?.SetScreenshotMode(true,
                "Screenshot mode: double-click the first corner on screen (click ▣ again to cancel)");
        }
    }

    /// <summary>
    /// Toolbar button: arms the region selector for Google AI Search.
    /// Selecting a region sends the screenshot to the internet-search-api on port 8002
    /// instead of the configured LLM.
    /// </summary>
    public void ToggleGoogleSearchMode()
    {
        // If a capture session is active, G sends the batch instead of starting region selection
        if (TrySendCaptureSessionViaGoogle()) return;

        if (_regionSelection is null) return;
        if (_regionSelection.IsArmed && _googleSearchMode)
        {
            _regionSelection.Disarm();
            _googleSearchMode = false;
            _overlayVm?.SetGoogleSearchMode(false);
            _overlayVm?.SetScreenshotMode(false, string.Empty);
        }
        else
        {
            // Disarm any prior screenshot mode first
            if (_regionSelection.IsArmed) _regionSelection.Disarm();
            _googleSearchMode = true;
            _regionSelection.Arm();
            _overlayVm?.SetGoogleSearchMode(true);
            _overlayVm?.SetScreenshotMode(true,
                "Google Search: double-click the first corner, then the opposite corner (click G again to cancel)");
        }
    }

    /// <summary>
    /// S button:
    ///   - If no session active → show snip UI to pin a region, start session
    ///   - If session active and not full → capture a shot of the pinned region + flash
    ///   - If session full → do nothing (user must send or cancel via G/LLM buttons, or cancel by long-press — same button cancels)
    ///   - Second press while drawing region → cancel
    /// The session ends automatically when the user presses the Local LLM or G button.
    /// </summary>
    /// <summary>
    /// S button state machine — uses the same two-double-click region picker as ▣ and G,
    /// no overlay, no cursor change, nothing visible during screen share.
    ///
    ///   No session active, not armed  → arm the picker; user double-clicks two corners
    ///   No session active, armed      → cancel the pending pick (second S press)
    ///   Session active, not full      → add a shot of the pinned region
    ///   Session active, full          → cancel the session (S acts as abort)
    /// </summary>
    public void HandleCaptureSessionButtonClick()
    {
        if (_overlayVm is null || _regionSelection is null) return;

        if (_overlayVm.IsCaptureSessionActive)
        {
            if (_overlayVm.IsCaptureSessionFull)
                CancelCaptureSession();
            else
                _ = AddCaptureSessionShotAsync();
            return;
        }

        if (_regionSelection.IsArmed && _captureSessionMode)
        {
            // Second S press while waiting for corner picks → cancel
            _regionSelection.Disarm();
            _captureSessionMode = false;
            _overlayVm.SetScreenshotMode(false, string.Empty);
        }
        else
        {
            if (_regionSelection.IsArmed) _regionSelection.Disarm();
            _captureSessionMode = true;
            _googleSearchMode = false;
            _regionSelection.Arm();
            _overlayVm.SetScreenshotMode(true,
                "Session: double-click first corner, then double-click opposite corner");
        }
    }

    private async Task AddCaptureSessionShotAsync()
    {
        if (_orchestrator is null || _overlayVm is null) return;
        if (!_overlayVm.IsCaptureSessionActive || _overlayVm.IsCaptureSessionFull) return;

        var (imageBase64, thumbnail) = await _orchestrator.CaptureSessionShotAsync(_sessionRegion);
        if (imageBase64 is null) return;

        _overlayVm.AddCaptureSessionShot(imageBase64, thumbnail!);
    }

    private void CancelCaptureSession()
    {
        _captureSessionMode = false;
        _overlayVm?.EndCaptureSession();
        _regionBorderWindow?.Close();
        _regionBorderWindow = null;
    }

    /// <summary>
    /// Sends the current capture session batch to the configured LLM, then ends the session.
    /// Called when the user clicks the Local LLM button while a session is active.
    /// Falls through to normal LLM screenshot mode if no session is active.
    /// </summary>
    public bool TrySendCaptureSessionViaLlm()
    {
        if (_overlayVm is null || !_overlayVm.IsCaptureSessionActive) return false;
        var images = _overlayVm.GetCaptureSessionImages();
        CancelCaptureSession();
        _ = _orchestrator?.HandleCaptureSessionLlmAsync(images);
        return true;
    }

    /// <summary>
    /// Sends the current capture session batch to Google AI Search, then ends the session.
    /// Called when the user clicks the G button while a session is active.
    /// Falls through to normal Google Search mode if no session is active.
    /// </summary>
    public bool TrySendCaptureSessionViaGoogle()
    {
        if (_overlayVm is null || !_overlayVm.IsCaptureSessionActive) return false;
        var images = _overlayVm.GetCaptureSessionImages();
        CancelCaptureSession();
        _ = _orchestrator?.HandleCaptureSessionGoogleAsync(images);
        return true;
    }

    public void ExportTranscript()
    {
        if (_orchestrator is null) return;
        var text = _orchestrator.GetTranscriptExportText();
        if (string.IsNullOrEmpty(text))
        {
            System.Windows.MessageBox.Show(
                "No transcript available yet — start capture and wait for speech.",
                "Chum — Export Transcript", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Export Transcript",
            Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*",
            FileName = $"chum_transcript_{DateTime.Now:yyyyMMdd_HHmmss}.txt",
            DefaultExt = ".txt"
        };
        if (dlg.ShowDialog() != true) return;

        File.WriteAllText(dlg.FileName, text);
        Log.Information("Transcript exported: {Path}", dlg.FileName);
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        Log.Information("Chum shutting down");
        _regionBorderWindow?.Close();
        _trayIcon?.Dispose();
        _hotkeys?.Dispose();
        _updateHttp.Dispose();
        if (_orchestrator is not null)
            await _orchestrator.StopAsync();
        _orchestrator?.Dispose();
        _regionSelection?.Dispose();
        _screenCapture?.Dispose();
        _clipboardMonitor?.Dispose();
        Settings.Save(); // persist overlay position and runtime setting changes
        Log.CloseAndFlush();
        base.OnExit(e);
    }

    private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        var ex = (Exception)e.ExceptionObject;
        Log.Fatal(ex, "Unhandled exception — terminating={Terminating}", e.IsTerminating);
        var transcriptPath = ExportEmergencyTranscript();
        if (!e.IsTerminating) return;
        var detail = transcriptPath is not null
            ? $"Transcript saved to:\n{transcriptPath}\n\n"
            : string.Empty;
        System.Windows.MessageBox.Show(
            $"Chum encountered an unexpected error and must close.\n\n{detail}Error: {ex.Message}",
            "Chum — Unexpected Error", MessageBoxButton.OK, MessageBoxImage.Error);
    }

    private void OnDispatcherUnhandledException(object sender,
        System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error(e.Exception, "Unhandled exception on UI thread — recovering");
        _overlayVm?.ShowError($"UI error — check logs: {e.Exception.Message}");
        e.Handled = true;
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        Log.Warning(e.Exception, "Unobserved task exception");
        e.SetObserved();
    }

    private string? ExportEmergencyTranscript()
    {
        if (_orchestrator is null) return null;
        try
        {
            var text = _orchestrator.GetTranscriptExportText();
            if (string.IsNullOrEmpty(text)) return null;
            var path = Path.Combine(Path.GetTempPath(),
                $"chum_transcript_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
            File.WriteAllText(path, text);
            Log.Information("Emergency transcript exported: {Path}", path);
            return path;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to export emergency transcript");
            return null;
        }
    }

    // -----------------------------------------------------------------------
    // Auto-updater
    // -----------------------------------------------------------------------

    private async Task RunUpdateCheckAsync(CancellationToken ct)
    {
        try
        {
            if (_updateChecker is null || !Settings.Current.CheckForUpdates) return;

            // At most once per 23 hours
            var age = DateTimeOffset.UtcNow - Settings.Current.LastUpdateCheckUtc;
            if (age.TotalHours < 23) return;

            Log.Information("UpdateChecker: checking for updates in background");
            var info = await _updateChecker.CheckForUpdateAsync(ct);
            Settings.Update(s => s.LastUpdateCheckUtc = DateTimeOffset.UtcNow);
            if (info is null) return;

            await Dispatcher.InvokeAsync(() => ShowUpdateAvailable(info));
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "UpdateChecker: background check failed");
        }
    }

    private async Task CheckForUpdatesManuallyAsync()
    {
        if (_updateChecker is null) return;
        if (_pendingUpdate is not null) { await ApplyUpdateAsync(); return; }

        Log.Information("UpdateChecker: manual check triggered");
        var info = await _updateChecker.CheckForUpdateAsync();
        Settings.Update(s => s.LastUpdateCheckUtc = DateTimeOffset.UtcNow);
        if (info is null)
        {
            _trayIcon?.ShowBalloonTip(4000, "Chum", "You're up to date.", ToolTipIcon.Info);
            return;
        }
        ShowUpdateAvailable(info);
    }

    private void ShowUpdateAvailable(UpdateInfo info)
    {
        _pendingUpdate = info;

        // Insert a highlighted item at the top of the tray menu
        if (_trayIcon?.ContextMenuStrip is not null && _updateMenuItem is null)
        {
            _updateMenuItem = new ToolStripMenuItem($"Update to v{info.Version}…")
            {
                ForeColor = System.Drawing.Color.DarkBlue,
                Font      = new System.Drawing.Font("Segoe UI", 9f, System.Drawing.FontStyle.Bold)
            };
            _updateMenuItem.Click += async (_, _) => await ApplyUpdateAsync();
            _trayIcon.ContextMenuStrip.Items.Insert(0, new ToolStripSeparator());
            _trayIcon.ContextMenuStrip.Items.Insert(0, _updateMenuItem);
        }

        // Tray balloon — click fires BalloonTipClicked which calls ApplyUpdateAsync
        _trayIcon?.ShowBalloonTip(
            timeout: 12000,
            tipTitle: $"Chum v{info.Version} is available",
            tipText: string.IsNullOrEmpty(info.ReleaseNotes)
                ? "Click to download and install."
                : $"{info.ReleaseNotes}\n\nClick to install.",
            tipIcon: ToolTipIcon.Info);

        Log.Information("UpdateChecker: notified user of v{Ver}", info.Version);
    }

    private async Task ApplyUpdateAsync()
    {
        if (_pendingUpdate is null || _updateChecker is null) return;
        var info = _pendingUpdate;

        if (_updateMenuItem is not null)
            _updateMenuItem.Text = "Downloading update…";

        var progress = new Progress<int>(pct =>
        {
            if (_updateMenuItem is not null)
                _updateMenuItem.Text = $"Downloading… {pct}%";
        });

        var ok = await _updateChecker.DownloadAndLaunchAsync(info, progress);
        if (ok)
        {
            Log.Information("UpdateChecker: installer launched — shutting down");
            Shutdown();
        }
        else
        {
            _pendingUpdate = info; // allow retry
            if (_updateMenuItem is not null)
                _updateMenuItem.Text = $"Update to v{info.Version}… (try again)";
            _trayIcon?.ShowBalloonTip(6000, "Chum update",
                "Download failed. Check your connection and try again via the tray menu.",
                ToolTipIcon.Warning);
        }
    }

    private static void ConfigureLogging()
    {
        var logDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Chum", "Logs");
        Directory.CreateDirectory(logDir);

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.File(
                Path.Combine(logDir, "chum-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7)
            .CreateLogger();
    }
}

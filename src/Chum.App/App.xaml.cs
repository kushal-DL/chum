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
using Whisper.net.Ggml;

namespace Chum.App;

public partial class App : System.Windows.Application
{
    // Publicly accessible services (used by SettingsWindow)
    public SettingsService Settings { get; } = new();
    public CredentialService Credentials { get; } = new();
    public MeetingOrchestrator? Orchestrator => _orchestrator;

    private HotkeyService? _hotkeys;
    private MeetingOrchestrator? _orchestrator;
    private OverlayWindow? _overlayWindow;
    private OverlayViewModel? _overlayVm;
    private NotifyIcon? _trayIcon;
    private DxgiScreenCapture? _screenCapture;
    private ClipboardMonitor? _clipboardMonitor;
    private bool _started;
    private string? _pendingMeetingDeviceId;

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

        // Create overlay early so we can show startup status immediately
        _overlayVm = new OverlayViewModel(Dispatcher);
        _overlayWindow = new OverlayWindow(_overlayVm);

        bool localMode = Settings.Current.LocalOnlyMode;
        bool hasKey = localMode
                   || Credentials.GetAnthropicKey() is not null
                   || Credentials.GetOpenAiKey() is not null;
        if (!hasKey)
        {
            Log.Information("No API key found — showing settings on first run");
            var setup = new SettingsWindow();
            setup.ShowDialog();
            localMode = Settings.Current.LocalOnlyMode;
            hasKey = localMode
                  || Credentials.GetAnthropicKey() is not null
                  || Credentials.GetOpenAiKey() is not null;
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

        OpenAiSttProvider? cloudStt = null;
        if (Settings.Current.CloudSttFallback)
        {
            var openAiKey = Credentials.GetOpenAiKey();
            if (openAiKey is not null)
                cloudStt = new OpenAiSttProvider(openAiKey, Settings.Current.CloudSttModel);
            else
                Log.Warning("CloudSttFallback enabled but no OpenAI API key stored — fallback disabled");
        }

        await Task.WhenAll(vadTask1, vadTask2, templatesTask);

        var loopback = new LoopbackCapture(Settings.Current.LoopbackDeviceId);
        var mic = new MicCapture(Settings.Current.MicDeviceId);
        var audioPipeline = new AudioPipeline(loopback, mic, vadTask1.Result, vadTask2.Result);

        var modelType = Enum.TryParse<GgmlType>(Settings.Current.WhisperModel, out var gt) ? gt : GgmlType.Small;
        var modelDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Chum", "Models");
        bool hasGpu = DetectDedicatedGpu(out string gpuName);
        Log.Information("GPU detection: {HasGpu} — {GpuName}", hasGpu, gpuName);
        var stt = new WhisperSttEngine(modelDir, modelType);

        var retentionWindow = TimeSpan.FromMinutes(Settings.Current.TranscriptRetentionMinutes);
        var transcriptBuffer = new TranscriptBuffer(retentionWindow);
        var contextExtractor = new ContextExtractor(transcriptBuffer);

        _orchestrator = new MeetingOrchestrator(
            audioPipeline, stt, transcriptBuffer, contextExtractor,
            llm, _hotkeys, _overlayVm!, Settings, _screenCapture, _clipboardMonitor, cloudStt, templates);

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
        var newPipeline = new AudioPipeline(newLoopback, newMic, BuildVad(), BuildVad());
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
        var newPipeline = new AudioPipeline(new LoopbackCapture(null), new MicCapture(null), BuildVad(), BuildVad());
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
        var modelDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Chum", "Models");
        var sileroPath = Path.Combine(modelDir, "silero_vad.onnx");

        if (File.Exists(sileroPath))
        {
            Log.Information("SileroVad model found — using Silero VAD");
            return new SileroVad(sileroPath);
        }

        // Model not yet downloaded — start background download for next launch, use EnergyVad now
        Log.Information("Silero VAD model not found — using EnergyVad; downloading model in background (5s delay)");
        _ = Task.Run(async () =>
        {
            try
            {
                // Defer 5s so the download doesn't compete with app startup on the network/CPU
                await Task.Delay(TimeSpan.FromSeconds(5));
                var dl = new ModelDownloadService(new HttpClient());
                await dl.EnsureSileroAsync();
                Log.Information("Silero VAD model download complete — will use on next launch");
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Silero VAD model download failed — EnergyVad will be used");
            }
        });
        return new EnergyVad();
    }

    private ILlmProvider BuildLlmProvider()
    {
        var s = Settings.Current;

        if (s.LocalOnlyMode)
        {
            Log.Information("Local-only mode — using Ollama: {Model} at {Url}", s.OllamaModel, s.OllamaBaseUrl);
            return new OllamaLlmProvider(s.OllamaModel, s.OllamaBaseUrl);
        }

        var model = s.LlmModel;
        bool isOpenAi = model.StartsWith("gpt", StringComparison.OrdinalIgnoreCase);

        if (isOpenAi)
        {
            var key = Credentials.GetOpenAiKey();
            if (key is not null)
            {
                Log.Information("Using OpenAI provider: {Model}", model);
                return new OpenAiLlmProvider(key, model);
            }
            Log.Warning("OpenAI model selected but no key stored — falling back to Anthropic");
        }

        var anthropicKey = Credentials.GetAnthropicKey()
            ?? throw new InvalidOperationException("No API key configured — cannot start LLM provider");
        Log.Information("Using Anthropic provider: {Model}", model);
        return new AnthropicLlmProvider(anthropicKey, model);
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
            Text = "Chum — AI Meeting Co-pilot",
            Visible = true
        };

        var menu = new ContextMenuStrip();
        menu.Items.Add("Show Overlay", null, (_, _) => ShowOverlay());
        menu.Items.Add("Settings", null, (_, _) => OpenSettings());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Start Capture", null, async (_, _) => await StartCaptureAsync());
        menu.Items.Add("Stop Capture", null, async (_, _) =>
        {
            if (_orchestrator is not null) await _orchestrator.StopAsync();
            _started = false;
        });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Export Transcript…", null, (_, _) => ExportTranscript());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Quit Chum", null, (_, _) => Shutdown());

        _trayIcon.ContextMenuStrip = menu;
        _trayIcon.DoubleClick += (_, _) => ShowOverlay();
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
        _trayIcon?.Dispose();
        _hotkeys?.Dispose();
        if (_orchestrator is not null)
            await _orchestrator.StopAsync();
        _orchestrator?.Dispose();
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

    /// <summary>
    /// Enumerates DXGI adapters and returns true if a dedicated GPU with ≥500 MB VRAM is found.
    /// The GPU name is written to <paramref name="gpuName"/>. Uses Vortice.DXGI (already in scope
    /// for DxgiScreenCapture). Returns false + empty name in VMs or on systems with no discrete GPU.
    /// </summary>
    private static bool DetectDedicatedGpu(out string gpuName)
    {
        gpuName = "none";
        try
        {
            using var factory = Vortice.DXGI.DXGI.CreateDXGIFactory1<Vortice.DXGI.IDXGIFactory1>();
            for (int i = 0; factory.EnumAdapters1(i, out var adapter).Success; i++)
            {
                using (adapter)
                {
                    var desc = adapter.Description1;
                    // Skip the "Microsoft Basic Render Driver" (software adapter)
                    if ((desc.Flags & Vortice.DXGI.AdapterFlags.Software) != 0) continue;
                    long vram = (long)desc.DedicatedVideoMemory;
                    if (vram >= 500 * 1024 * 1024) // 500 MB minimum for Whisper small
                    {
                        gpuName = $"{desc.Description.TrimEnd('\0')} ({vram / (1024 * 1024)} MB VRAM)";
                        return true;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "GPU detection via DXGI failed — assuming CPU-only");
        }
        return false;
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

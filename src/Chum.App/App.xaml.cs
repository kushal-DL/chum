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

    private HotkeyService? _hotkeys;
    private MeetingOrchestrator? _orchestrator;
    private OverlayWindow? _overlayWindow;
    private OverlayViewModel? _overlayVm;
    private NotifyIcon? _trayIcon;
    private DxgiScreenCapture? _screenCapture;
    private ClipboardMonitor? _clipboardMonitor;
    private bool _started;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        ConfigureLogging();
        Log.Information("Chum starting up");

        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        Settings.Load();

        // Local-only mode bypasses cloud API key requirement
        bool localMode = Settings.Current.LocalOnlyMode;

        // Show settings immediately if no API key configured (either provider) and not in local mode
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

        BuildAndWireComponents();
        ApplyCaptureExclusionToOverlay();
        CreateTrayIcon();

        if (!Settings.Current.StartMinimisedToTray)
            _overlayWindow!.Show();
        else
            _overlayWindow!.Show(); // show briefly then hide -- user can toggle via tray

        if (Settings.Current.StartCapturingOnLaunch)
            await StartCaptureAsync();
    }

    private void BuildAndWireComponents()
    {
        _overlayVm = new OverlayViewModel(Dispatcher);
        _overlayWindow = new OverlayWindow(_overlayVm);

        var loopback = new LoopbackCapture(Settings.Current.LoopbackDeviceId);
        var mic = new MicCapture(Settings.Current.MicDeviceId);
        var audioPipeline = new AudioPipeline(loopback, mic, BuildVad(), BuildVad());

        var modelType = Enum.TryParse<GgmlType>(Settings.Current.WhisperModel, out var gt) ? gt : GgmlType.Small;
        var modelDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Chum", "Models");
        var stt = new WhisperSttEngine(modelDir, modelType);

        var retentionWindow = TimeSpan.FromMinutes(Settings.Current.TranscriptRetentionMinutes);
        var transcriptBuffer = new TranscriptBuffer(retentionWindow);
        var contextExtractor = new ContextExtractor(transcriptBuffer);

        ILlmProvider llm = BuildLlmProvider();

        _hotkeys = new HotkeyService();
        RegisterHotkeys();
        _hotkeys.Install();

        DxgiScreenCapture.TryCreate(out _screenCapture);
        _clipboardMonitor = new ClipboardMonitor();

        _orchestrator = new MeetingOrchestrator(
            audioPipeline, stt, transcriptBuffer, contextExtractor,
            llm, _hotkeys, _overlayVm, Settings, _screenCapture, _clipboardMonitor);

        _overlayWindow.ImageFileDropped += (_, path) =>
            _ = _orchestrator.HandleDroppedImageQueryAsync(path);

        _orchestrator.DeviceDisconnected += async (_, _) =>
            await FallbackToDefaultAudioAsync();

        // Opacity binding
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
        Log.Information("Silero VAD model not found — using EnergyVad; downloading model in background");
        _ = Task.Run(async () =>
        {
            try
            {
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

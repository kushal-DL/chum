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

        Settings.Load();

        // Show settings immediately if no API key configured
        var apiKey = Credentials.GetAnthropicKey();
        if (apiKey is null)
        {
            Log.Information("No API key found — showing settings on first run");
            var setup = new SettingsWindow();
            setup.ShowDialog();
            apiKey = Credentials.GetAnthropicKey();
            if (apiKey is null)
            {
                Log.Warning("No API key provided — exiting");
                Shutdown();
                return;
            }
        }

        BuildAndWireComponents(apiKey);
        ApplyCaptureExclusionToOverlay();
        CreateTrayIcon();

        if (!Settings.Current.StartMinimisedToTray)
            _overlayWindow!.Show();
        else
            _overlayWindow!.Show(); // show briefly then hide -- user can toggle via tray

        if (Settings.Current.StartCapturingOnLaunch)
            await StartCaptureAsync();
    }

    private void BuildAndWireComponents(string apiKey)
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

        ILlmProvider llm = new AnthropicLlmProvider(apiKey, Settings.Current.LlmModel);

        _hotkeys = new HotkeyService();
        RegisterHotkeys();
        _hotkeys.Install();

        DxgiScreenCapture.TryCreate(out _screenCapture);
        _clipboardMonitor = new ClipboardMonitor();

        _orchestrator = new MeetingOrchestrator(
            audioPipeline, stt, transcriptBuffer, contextExtractor,
            llm, _hotkeys, _overlayVm, Settings, _screenCapture, _clipboardMonitor);

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

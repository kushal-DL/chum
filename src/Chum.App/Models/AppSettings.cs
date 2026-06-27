namespace Chum.App.Models;

public sealed class AppSettings
{
    // --- LLM ---
    public string LlmProvider { get; set; } = "Anthropic";
    public string LlmModel { get; set; } = "claude-haiku-4-5-20251001";
    public int MaxResponseTokens { get; set; } = 1024;
    public float Temperature { get; set; } = 0.3f;
    public string? UserName { get; set; }

    // --- Audio ---
    public string? LoopbackDeviceId { get; set; }   // null = Windows default
    public string? MicDeviceId { get; set; }          // null = Windows default
    public int TranscriptRetentionMinutes { get; set; } = 10;

    // --- Transcription ---
    public string WhisperModel { get; set; } = "Small";  // GgmlType enum name
    public bool UseGpu { get; set; } = true;

    // --- Hotkeys (string representation, e.g. "Ctrl+Alt+Space") ---
    public string HoldToAskHotkey { get; set; } = "Ctrl+Alt+Space";
    public string ScreenCaptureHotkey { get; set; } = "Ctrl+Alt+S";
    public string PrivacyPauseHotkey { get; set; } = "Ctrl+Alt+P";
    public string HideOverlayHotkey { get; set; } = "Ctrl+Alt+H";
    public string ActionItemsHotkey { get; set; } = "Ctrl+Alt+A";

    // --- Overlay appearance ---
    public double OverlayOpacity { get; set; } = 0.85;
    public double OverlayLeft { get; set; } = -1;   // -1 = auto position (bottom-right)
    public double OverlayTop { get; set; } = -1;
    public double OverlayWidth { get; set; } = 420;
    public double OverlayHeight { get; set; } = 320;
    public int FontSize { get; set; } = 13;

    // --- Behaviour ---
    public bool StartWithWindows { get; set; } = false;
    public bool StartMinimisedToTray { get; set; } = true;
    public bool StartCapturingOnLaunch { get; set; } = true;
    public bool AutoHideOnScreenShare { get; set; } = true;
    // Auto-start/stop capture when a meeting app (Teams, Zoom, Meet) opens/closes
    public bool AutoStartCapture { get; set; } = false;

    // --- Overlay capture privacy ---
    // Applies SetWindowDisplayAffinity(WDA_EXCLUDEFROMCAPTURE) to hide the overlay from screen
    // captures, recordings, and screen-share streams while still showing on your physical display.
    // Requires Windows 10 2004+. Default: on — most users want this.
    public bool ExcludeFromScreenCapture { get; set; } = true;

    // --- Screen capture privacy ---
    // When true: first Ctrl+Alt+S shows a confirmation banner; second press within 5s actually captures.
    // Prevents accidental screen captures that send sensitive content to the cloud LLM.
    public bool ConfirmScreenCapture { get; set; } = false;

    // --- Cloud STT fallback (OpenAI Whisper API) ---
    // When true: if local Whisper is not ready or throws, fall back to the cloud provider.
    public bool CloudSttFallback { get; set; } = false;
    public string CloudSttModel { get; set; } = "whisper-1";

    // --- Local-only mode (Ollama) ---
    public bool LocalOnlyMode { get; set; } = false;
    public string OllamaModel { get; set; } = "llama3.1:8b";
    public string OllamaBaseUrl { get; set; } = "http://localhost:11434";

    // --- Cost tracking ---
    // Warn in overlay when session API spend exceeds this threshold. 0 = disabled.
    public decimal SpendThresholdDollars { get; set; } = 1.00m;

    // --- Prompt templates ---
    public string ActiveTemplateName { get; set; } = "Default";

    // --- Privacy disclosure ---
    // Show a reminder banner when capture starts to inform participants of AI assistance.
    // After the user dismisses it the first time, this is set to false automatically.
    public bool ShowDisclosureReminder { get; set; } = true;

    // --- Teams captions (US-06-05 / US-09-05) ---
    // When enabled, Chum reads Teams' own live caption text via Windows UI Automation
    // and merges it into the transcript buffer as a supplementary source.
    // Off by default: requires Teams auto-captions to be enabled by the organiser.
    public bool UseTeamsCaptions { get; set; } = false;

    // --- Auto-update (US-10-09) ---
    // When true: check GitHub Releases on startup (at most once per day).
    public bool CheckForUpdates { get; set; } = true;
    // UTC timestamp of the last successful version check (persisted so we respect the daily limit).
    public DateTimeOffset LastUpdateCheckUtc { get; set; } = DateTimeOffset.MinValue;

    // --- Crash reporting (US-10-08) ---
    // When true: on unhandled exception, write a structured crash report to
    // %LOCALAPPDATA%\Chum\CrashReports\ and show a dialog offering the user to
    // open the folder so they can share the file with support. Default: off (opt-in).
    public bool EnableCrashReporting { get; set; } = false;

    // --- Low-power mode (US-10-10) ---
    // When true: auto-activate low-power mode on battery (detected via SystemInformation.PowerStatus).
    // When on battery in low-power mode: GC timer interval doubles; overlay shows "Low power" badge.
    // Next restart with low-power mode active will select the "Base" Whisper model automatically
    // (smaller, faster, lower CPU) rather than the user-selected model.
    public bool AutoLowPowerOnBattery { get; set; } = true;
    // Manual override: forces low-power mode regardless of power state.
    public bool ForceLowPowerMode { get; set; } = false;
}

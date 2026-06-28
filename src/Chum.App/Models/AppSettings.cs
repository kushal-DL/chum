namespace Chum.App.Models;

/// <summary>How a press-to-record query is processed when the user stops recording.</summary>
public enum QueryMode
{
    /// <summary>Transcribe the recording locally (Sherpa), then send the text to the LLM. Fast, private.</summary>
    LocalTranscribeToLlm,
    /// <summary>Send the recorded audio (WAV) directly to a multimodal LLM that accepts audio input.</summary>
    AudioToLlm,
}

public sealed class AppSettings
{
    // --- LLM ---
    public string LlmProvider { get; set; } = "Anthropic";
    public string LlmModel { get; set; } = "claude-haiku-4-5-20251001";
    public string LlmApiBaseUrl { get; set; } = "";
    public int MaxResponseTokens { get; set; } = 1024;
    public float Temperature { get; set; } = 0.3f;
    public string? UserName { get; set; }

    // --- Audio ---
    public string? LoopbackDeviceId { get; set; }   // null = Windows default
    public string? MicDeviceId { get; set; }          // null = Windows default
    public int TranscriptRetentionMinutes { get; set; } = 10;

    // --- Transcription ---
    // Local STT engine: sherpa-onnx streaming Zipformer (fast, runs on CPU at ~10x real-time).
    // Used for the rolling transcript. For press-to-record queries, see CloudSttFallback below.

    // --- Query mode (press-to-record) ---
    // What happens when you press the hold-to-ask hotkey a second time to stop recording.
    public QueryMode QueryMode { get; set; } = QueryMode.LocalTranscribeToLlm;
    // Include the rolling meeting transcript as extra context with each query.
    // Off by default: a query sends ONLY your recorded question, not the whole meeting transcript.
    public bool IncludeTranscriptContext { get; set; } = false;

    // --- Noise suppression / VAD ---
    // Apply high-pass + adaptive noise gate to captured audio before STT/LLM.
    public bool EnableNoiseSuppression { get; set; } = true;
    // EnergyVad onset threshold in dBFS. Higher (e.g. -30) = less sensitive, better for noisy rooms.
    public float VadThresholdDb { get; set; } = -35f;

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

    // --- Cloud STT for press-to-record queries ---
    // When true: press-to-record queries are transcribed by the cloud API instead of local sherpa-onnx.
    // Use NVIDIA NIM (nvidia/canary-1b) or OpenAI (whisper-1) for significantly better accuracy on
    // technical vocabulary. Requires API key for the selected provider.
    public bool CloudSttFallback { get; set; } = false;
    // Model name sent to the cloud STT endpoint. NVIDIA: "nvidia/canary-1b". OpenAI: "whisper-1".
    public string CloudSttModel { get; set; } = "nvidia/canary-1b";
    // Base URL for cloud STT. Empty = OpenAI. Set to "https://integrate.api.nvidia.com/v1" for NVIDIA.
    public string CloudSttBaseUrl { get; set; } = "https://integrate.api.nvidia.com/v1";

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
    public bool AutoLowPowerOnBattery { get; set; } = true;
    // Manual override: forces low-power mode regardless of power state.
    public bool ForceLowPowerMode { get; set; } = false;
}

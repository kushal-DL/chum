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

    // --- Overlay capture privacy ---
    // Applies SetWindowDisplayAffinity(WDA_EXCLUDEFROMCAPTURE) to hide the overlay from screen
    // captures, recordings, and screen-share streams while still showing on your physical display.
    // Requires Windows 10 2004+. Default: on — most users want this.
    public bool ExcludeFromScreenCapture { get; set; } = true;
}

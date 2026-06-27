using System.Runtime.InteropServices;
using System.Text;
using Timer = System.Threading.Timer;

namespace Chum.App.Services;

/// <summary>
/// Polls Win32 window list every 2 s to detect when the user is screen-sharing
/// in Teams, Zoom, or Google Meet. Fires SharingStateChanged when state flips.
///
/// Note: WDA_EXCLUDEFROMCAPTURE in OverlayWindow already hides the overlay from
/// captured frames. This detector provides the belt-and-suspenders auto-hide
/// so the overlay also disappears visually when the user is presenting.
/// </summary>
public sealed class ScreenShareDetector : IDisposable
{
    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);
    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    // Fired on the thread-pool thread; callers must marshal to UI thread if needed.
    public event EventHandler<bool>? SharingStateChanged;

    private Timer? _timer;
    private bool _lastState;
    private bool _disposed;

    public void Start()
    {
        _timer = new Timer(_ => Poll(), null, TimeSpan.Zero, TimeSpan.FromSeconds(2));
    }

    private void Poll()
    {
        try
        {
            var sharing = IsScreenShareActive();
            if (sharing == _lastState) return;
            _lastState = sharing;
            SharingStateChanged?.Invoke(this, sharing);
        }
        catch { /* never crash the poll loop */ }
    }

    private static bool IsScreenShareActive()
    {
        bool found = false;
        EnumWindows((hwnd, _) =>
        {
            if (!IsWindowVisible(hwnd)) return true;

            var title = GetTitle(hwnd);
            var cls = GetClass(hwnd);

            // Teams Classic / New Teams screen-share toolbar
            if (IsTeamsSharing(title, cls)) { found = true; return false; }

            // Zoom share toolbar
            if (IsZoomSharing(cls)) { found = true; return false; }

            // Google Meet / other browser-based sharing:
            // Chrome/Edge shows a floating "You are sharing…" strip (class Chrome_WidgetWin_1)
            if (IsBrowserSharingStrip(title, cls)) { found = true; return false; }

            return true;
        }, IntPtr.Zero);
        return found;
    }

    private static bool IsTeamsSharing(string title, string cls)
    {
        // Teams creates a floating sharing-controls window when presenting.
        // Title contains "sharing" and is short (not a meeting window with a long title).
        if (title.Contains("sharing", StringComparison.OrdinalIgnoreCase) && title.Length < 60)
            return true;
        // New Teams (WebView2-hosted) control bar
        if (cls.Contains("TeamsWebView", StringComparison.OrdinalIgnoreCase) &&
            title.Contains("present", StringComparison.OrdinalIgnoreCase))
            return true;
        return false;
    }

    private static bool IsZoomSharing(string cls)
    {
        // Zoom's share toolbar has a distinctive class name
        return cls.Equals("zoom_sharetoolbar", StringComparison.OrdinalIgnoreCase) ||
               cls.Equals("ZPToolBarParentWnd", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsBrowserSharingStrip(string title, string cls)
    {
        // Chrome / Edge emits a small "You are sharing a tab/window" notification strip
        return (cls is "Chrome_WidgetWin_1" or "Chrome_WidgetWin_0") &&
               title.Contains("sharing", StringComparison.OrdinalIgnoreCase) &&
               title.Length < 80;
    }

    private static string GetTitle(IntPtr hwnd)
    {
        var sb = new StringBuilder(256);
        GetWindowText(hwnd, sb, 256);
        return sb.ToString();
    }

    private static string GetClass(IntPtr hwnd)
    {
        var sb = new StringBuilder(256);
        GetClassName(hwnd, sb, 256);
        return sb.ToString();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _timer?.Dispose();
    }
}

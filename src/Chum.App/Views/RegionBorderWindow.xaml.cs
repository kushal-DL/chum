using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Chum.App.Views;

/// <summary>
/// A click-through always-on-top border that frames a pinned capture region.
/// Excluded from all screen captures (WDA_EXCLUDEFROMCAPTURE) and screen shares.
/// </summary>
public partial class RegionBorderWindow : Window
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowDisplayAffinity(IntPtr hwnd, uint dwAffinity);
    private const uint WDA_EXCLUDEFROMCAPTURE = 0x00000011;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowLong(IntPtr hwnd, int nIndex, int dwNewLong);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr hwnd, int nIndex);
    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
        int x, int y, int cx, int cy, uint uFlags);

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WS_EX_LAYERED = 0x00080000;
    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private const uint SWP_NOACTIVATE = 0x0010;

    private System.Drawing.Rectangle _physicalRegion;

    public RegionBorderWindow()
    {
        InitializeComponent();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;

        // Make the window click-through at Win32 level (WPF IsHitTestVisible alone isn't enough)
        var exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
        SetWindowLong(hwnd, GWL_EXSTYLE, exStyle | WS_EX_TRANSPARENT | WS_EX_LAYERED);

        // Exclude from every screen capture / share — GPU-level, universal across all apps
        SetWindowDisplayAffinity(hwnd, WDA_EXCLUDEFROMCAPTURE);

        PositionOverRegion(hwnd);
    }

    /// <summary>Positions the window over the given physical-pixel region and shows it.</summary>
    public void ShowOverRegion(System.Drawing.Rectangle physicalRegion)
    {
        _physicalRegion = physicalRegion;

        // Set approximate WPF logical size before Show() so layout is sane;
        // OnSourceInitialized corrects the position via SetWindowPos in physical pixels.
        Width  = physicalRegion.Width;
        Height = physicalRegion.Height;
        Left   = physicalRegion.Left;
        Top    = physicalRegion.Top;

        Show();
    }

    private void PositionOverRegion(IntPtr hwnd)
    {
        // Use SetWindowPos to place in exact physical screen pixels regardless of DPI scaling.
        // SWP_NOACTIVATE keeps focus on the user's active window.
        SetWindowPos(hwnd, HWND_TOPMOST,
            _physicalRegion.Left, _physicalRegion.Top,
            _physicalRegion.Width, _physicalRegion.Height,
            SWP_NOACTIVATE);
    }
}

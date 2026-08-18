using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace Chum.App.Services;

/// <summary>
/// Global low-level mouse hook (WH_MOUSE_LL) used by "screenshot mode".
///
/// When armed, it waits for two double-clicks anywhere on screen. The two clicked
/// points are treated as opposite corners of the diagonal of a rectangle:
///   • 1st double-click → first corner (FirstPointCaptured fires)
///   • 2nd double-click → opposite corner → RegionSelected fires with the bounding
///     rectangle (in physical screen pixels) and the hook disarms itself.
///
/// The hook is only installed while armed, so it adds no global input overhead the
/// rest of the time. Double-clicks are NOT suppressed, so the overlay's own buttons
/// (e.g. clicking the screenshot button again to cancel) keep working.
///
/// Coordinates: MSLLHOOKSTRUCT.pt is in physical screen pixels — the same space
/// DxgiScreenCapture.CaptureRegionAsJpegBase64 expects — so no DPI conversion is needed.
/// </summary>
public sealed class RegionSelectionService : IDisposable
{
    private const int WH_MOUSE_LL = 14;
    private const int WM_LBUTTONDOWN = 0x0201;
    private const int SM_CXDOUBLECLK = 36;
    private const int SM_CYDOUBLECLK = 37;

    [DllImport("user32.dll")] private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hhk);
    [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll")] private static extern IntPtr GetModuleHandle(string? lpModuleName);
    [DllImport("user32.dll")] private static extern uint GetDoubleClickTime();
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int nIndex);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int x; public int y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSLLHOOKSTRUCT { public POINT pt; public uint mouseData; public uint flags; public uint time; public IntPtr dwExtraInfo; }

    private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

    private readonly LowLevelMouseProc _proc;
    private readonly Dispatcher _dispatcher;
    private IntPtr _hookId = IntPtr.Zero;

    private bool _haveFirst;
    private POINT _first;
    private int _lastDownTick;
    private POINT _lastDownPt;

    /// <summary>True while the hook is installed and waiting for double-clicks.</summary>
    public bool IsArmed { get; private set; }

    /// <summary>Fires (on the UI thread) after the first corner double-click is captured.</summary>
    public event EventHandler? FirstPointCaptured;

    /// <summary>Fires (on the UI thread) with the selected region in physical screen pixels.</summary>
    public event EventHandler<Rectangle>? RegionSelected;

    public RegionSelectionService(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
        _proc = HookCallback; // keep delegate alive for the lifetime of the hook
    }

    /// <summary>Install the hook and begin waiting for two corner double-clicks. Call on the UI thread.</summary>
    public void Arm()
    {
        _haveFirst = false;
        _lastDownTick = 0;
        if (_hookId == IntPtr.Zero)
        {
            using var proc = Process.GetCurrentProcess();
            using var module = proc.MainModule!;
            _hookId = SetWindowsHookEx(WH_MOUSE_LL, _proc, GetModuleHandle(module.ModuleName), 0);
            if (_hookId == IntPtr.Zero)
                Serilog.Log.Error("Failed to install mouse hook — screenshot region selection unavailable");
            else
                Serilog.Log.Information("Screenshot mode armed — mouse hook installed");
        }
        IsArmed = _hookId != IntPtr.Zero;
    }

    /// <summary>Cancel selection and remove the hook.</summary>
    public void Disarm()
    {
        IsArmed = false;
        _haveFirst = false;
        if (_hookId != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hookId);
            _hookId = IntPtr.Zero;
            Serilog.Log.Information("Screenshot mode disarmed — mouse hook removed");
        }
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && IsArmed && (int)wParam == WM_LBUTTONDOWN)
        {
            var info = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
            int now = Environment.TickCount;

            bool isDouble =
                _lastDownTick != 0 &&
                (now - _lastDownTick) <= (int)GetDoubleClickTime() &&
                Math.Abs(info.pt.x - _lastDownPt.x) <= GetSystemMetrics(SM_CXDOUBLECLK) &&
                Math.Abs(info.pt.y - _lastDownPt.y) <= GetSystemMetrics(SM_CYDOUBLECLK);

            _lastDownTick = now;
            _lastDownPt = info.pt;

            if (isDouble)
            {
                _lastDownTick = 0; // reset so a triple-click doesn't chain into a new pair
                HandleDoubleClick(info.pt);
            }
        }
        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    private void HandleDoubleClick(POINT pt)
    {
        if (!_haveFirst)
        {
            _first = pt;
            _haveFirst = true;
            _dispatcher.BeginInvoke(() => FirstPointCaptured?.Invoke(this, EventArgs.Empty));
        }
        else
        {
            var rect = new Rectangle(
                Math.Min(_first.x, pt.x),
                Math.Min(_first.y, pt.y),
                Math.Abs(pt.x - _first.x),
                Math.Abs(pt.y - _first.y));
            _haveFirst = false;
            Disarm(); // selection complete — stop listening
            _dispatcher.BeginInvoke(() => RegionSelected?.Invoke(this, rect));
        }
    }

    public void Dispose() => Disarm();
}

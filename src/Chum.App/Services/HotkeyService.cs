using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Input;

namespace Chum.App.Services;

/// <summary>
/// Global keyboard hook using WH_KEYBOARD_LL.
/// Must be installed on the WPF UI thread (which already has a Win32 message pump).
///
/// Hold-to-ask flow:
///   KeyDown → HoldStarted fires (with timestamp)
///   KeyUp   → QueryFired fires (with start + end timestamps)
///   Debounce: holds shorter than 300 ms are discarded.
/// </summary>
public sealed class HotkeyService : IDisposable
{
    // Win32 constants
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_KEYUP = 0x0101;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_SYSKEYUP = 0x0105;

    [DllImport("user32.dll")] private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hhk);
    [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll")] private static extern IntPtr GetModuleHandle(string? lpModuleName);

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT { public uint vkCode; public uint scanCode; public uint flags; public uint time; public IntPtr dwExtraInfo; }

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    // Registered hotkeys: key = virtual key code, value = set of required modifiers
    private record HotkeyDef(Key Key, ModifierKeys Modifiers, string ActionId);
    private readonly List<HotkeyDef> _hotkeys = [];

    // Toggle-to-ask state for HoldToAsk hotkey (press once = start, press again = stop+fire)
    // Other hotkeys (ActionItems, ScreenCapture, etc.) use tap-on-key-up behaviour.
    private bool _keyDown;           // true while any registered hotkey key is physically held (suppresses key-repeat)
    private bool _toggleRecording;   // HoldToAsk: true = currently recording; second press fires QueryFired
    private DateTimeOffset _toggleStart;
    private string? _activeActionId;

    private IntPtr _hookId = IntPtr.Zero;
    private readonly LowLevelKeyboardProc _hookProc;

    public event EventHandler<HotkeyHoldEventArgs>? HoldStarted;
    public event EventHandler<HotkeyQueryEventArgs>? QueryFired;
    public event EventHandler<string>? HotkeyTapped; // for non-hold hotkeys

    public HotkeyService()
    {
        _hookProc = HookCallback; // keep delegate alive
    }

    public void Install()
    {
        using var proc = Process.GetCurrentProcess();
        using var module = proc.MainModule!;
        _hookId = SetWindowsHookEx(WH_KEYBOARD_LL, _hookProc, GetModuleHandle(module.ModuleName), 0);
        if (_hookId == IntPtr.Zero)
            Serilog.Log.Error("Failed to install keyboard hook — hotkeys will not work");
        else
            Serilog.Log.Information("Keyboard hook installed");
    }

    public void Uninstall()
    {
        if (_hookId != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hookId);
            _hookId = IntPtr.Zero;
        }
    }

    /// <summary>Register a hotkey. <paramref name="isHold"/> = true for hold-to-query.</summary>
    public void Register(string actionId, Key key, ModifierKeys modifiers)
    {
        _hotkeys.RemoveAll(h => h.ActionId == actionId);
        _hotkeys.Add(new HotkeyDef(key, modifiers, actionId));
    }

    /// <summary>Parses "Ctrl+Alt+Space" style strings and registers them.</summary>
    public void RegisterFromString(string actionId, string combo)
    {
        if (!TryParseCombo(combo, out var key, out var mods))
        {
            Serilog.Log.Warning("Could not parse hotkey combo '{Combo}' for action {Action}", combo, actionId);
            return;
        }
        Register(actionId, key, mods);
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var info = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            bool isDown = wParam == WM_KEYDOWN || wParam == WM_SYSKEYDOWN;
            bool isUp = wParam == WM_KEYUP || wParam == WM_SYSKEYUP;

            var pressedKey = KeyInterop.KeyFromVirtualKey((int)info.vkCode);
            var mods = GetCurrentModifiers();

            foreach (var hotkey in _hotkeys)
            {
                if (pressedKey != hotkey.Key) continue;
                if (mods != hotkey.Modifiers) continue;

                if (isDown && !_keyDown)
                {
                    _keyDown = true;

                    if (hotkey.ActionId == "HoldToAsk")
                    {
                        if (!_toggleRecording)
                        {
                            // First press: START recording
                            _toggleRecording = true;
                            _toggleStart = DateTimeOffset.UtcNow;
                            _activeActionId = hotkey.ActionId;
                            HoldStarted?.Invoke(this, new HotkeyHoldEventArgs(hotkey.ActionId, _toggleStart));
                        }
                        else if (_activeActionId == hotkey.ActionId)
                        {
                            // Second press: STOP recording and fire query
                            _toggleRecording = false;
                            var end = DateTimeOffset.UtcNow;
                            QueryFired?.Invoke(this, new HotkeyQueryEventArgs(hotkey.ActionId, _toggleStart, end));
                            _activeActionId = null;
                        }
                    }
                    else
                    {
                        _activeActionId = hotkey.ActionId;
                        // Tap actions fire on key-up
                    }
                }
                else if (isUp && _keyDown)
                {
                    _keyDown = false;

                    // Tap actions (ActionItems, ScreenCapture, etc.) fire on key-up
                    if (_activeActionId != null && _activeActionId != "HoldToAsk")
                    {
                        HotkeyTapped?.Invoke(this, _activeActionId);
                        _activeActionId = null;
                    }
                }
                break;
            }
        }
        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    private static ModifierKeys GetCurrentModifiers()
    {
        ModifierKeys mods = ModifierKeys.None;
        if (Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl)) mods |= ModifierKeys.Control;
        if (Keyboard.IsKeyDown(Key.LeftAlt) || Keyboard.IsKeyDown(Key.RightAlt)) mods |= ModifierKeys.Alt;
        if (Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift)) mods |= ModifierKeys.Shift;
        if (Keyboard.IsKeyDown(Key.LWin) || Keyboard.IsKeyDown(Key.RWin)) mods |= ModifierKeys.Windows;
        return mods;
    }

    private static bool TryParseCombo(string combo, out Key key, out ModifierKeys mods)
    {
        key = Key.None;
        mods = ModifierKeys.None;
        var parts = combo.Split('+', StringSplitOptions.TrimEntries);
        foreach (var part in parts)
        {
            if (part.Equals("Ctrl", StringComparison.OrdinalIgnoreCase)) { mods |= ModifierKeys.Control; continue; }
            if (part.Equals("Alt", StringComparison.OrdinalIgnoreCase)) { mods |= ModifierKeys.Alt; continue; }
            if (part.Equals("Shift", StringComparison.OrdinalIgnoreCase)) { mods |= ModifierKeys.Shift; continue; }
            if (part.Equals("Win", StringComparison.OrdinalIgnoreCase)) { mods |= ModifierKeys.Windows; continue; }
            if (Enum.TryParse<Key>(part, true, out var k)) { key = k; continue; }
            return false;
        }
        return key != Key.None;
    }

    public void Dispose() => Uninstall();
}

public sealed class HotkeyHoldEventArgs(string actionId, DateTimeOffset startTime) : EventArgs
{
    public string ActionId { get; } = actionId;
    public DateTimeOffset StartTime { get; } = startTime;
}

public sealed class HotkeyQueryEventArgs(string actionId, DateTimeOffset startTime, DateTimeOffset endTime) : EventArgs
{
    public string ActionId { get; } = actionId;
    public DateTimeOffset StartTime { get; } = startTime;
    public DateTimeOffset EndTime { get; } = endTime;
}

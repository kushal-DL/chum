using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Chum.App.ViewModels;
using Application = System.Windows.Application;
using DataFormats = System.Windows.DataFormats;
using DragDropEffects = System.Windows.DragDropEffects;
using DragEventArgs = System.Windows.DragEventArgs;

namespace Chum.App.Views;

public partial class OverlayWindow : Window
{
    // Win32: prevent this window from appearing in any screen capture / recording / share frame.
    // WDA_EXCLUDEFROMCAPTURE requires Windows 10 2004+ (build 19041). Falls back silently on older builds.
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowDisplayAffinity(IntPtr hwnd, uint dwAffinity);
    private const uint WDA_NONE = 0x00000000;
    private const uint WDA_EXCLUDEFROMCAPTURE = 0x00000011;

    // Win32: suppress focus steal and Alt+Tab visibility.
    // WS_EX_NOACTIVATE  — clicking or dragging this window never activates it, so the meeting
    //                      app (Teams/Zoom/Meet) stays in the foreground and its title bar stays
    //                      active. Without this, every click on the overlay shifts focus, which
    //                      is immediately visible to everyone on the screen share.
    // WS_EX_TOOLWINDOW  — removes the window from Alt+Tab, so it can't be spotted by other
    //                      participants even if the user accidentally opens the switcher.
    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hwnd, int nIndex);
    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hwnd, int nIndex, int dwNewLong);
    private const int GWL_EXSTYLE       = -20;
    private const int WS_EX_NOACTIVATE  = 0x08000000;
    private const int WS_EX_TOOLWINDOW  = 0x00000080;

    private bool _isResizing;
    private System.Windows.Point _resizeDragStart;
    private double _resizeStartW, _resizeStartH;

    // Manual header-drag state (replaces DragMove() which activates the window and steals focus).
    private bool _isDragging;
    private System.Windows.Point _headerDragStart;
    private double _dragStartLeft, _dragStartTop;

    public new int FontSize => (DataContext as OverlayViewModel) is not null ? 13 : 13;

    // Fires on the UI thread with the validated file path of an image dropped onto the overlay.
    public event EventHandler<string>? ImageFileDropped;

    private static readonly HashSet<string> ImageExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tiff", ".tif", ".webp" };

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = GetDroppedImagePath(e) is not null ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void Window_Drop(object sender, DragEventArgs e)
    {
        var path = GetDroppedImagePath(e);
        if (path is not null)
            ImageFileDropped?.Invoke(this, path);
    }

    private static string? GetDroppedImagePath(DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return null;
        var files = e.Data.GetData(DataFormats.FileDrop) as string[];
        var first = files?.FirstOrDefault(f => ImageExtensions.Contains(Path.GetExtension(f)));
        return first;
    }

    public OverlayWindow(OverlayViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;

        // Give the orchestrator a way to hide/show the HWND during captures.
        // WDA_EXCLUDEFROMCAPTURE causes our overlay to appear black in DXGI/GDI;
        // hiding the HWND removes it from DWM composition and clears the artifact.
        viewModel.RegisterCaptureCallbacks(
            hideHwnd: () => this.Visibility = Visibility.Hidden,
            showHwnd: () => this.Visibility = Visibility.Visible
        );

        // Persist position across sessions so overlay stays on the user's chosen monitor
        LocationChanged += (_, _) => PersistWindowBounds();
        SizeChanged += (_, _) => PersistWindowBounds();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;

        // Never activate this window on click/drag so the meeting app keeps focus.
        // Also hide from Alt+Tab so participants can't spot it in the switcher.
        int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
        SetWindowLong(hwnd, GWL_EXSTYLE, exStyle | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);

        PositionInBottomRight();
        ApplyCaptureExclusion();
    }

    /// <summary>
    /// Toggle whether this window appears in screen captures. Called once on startup (from settings)
    /// and again if the user changes the setting at runtime.
    /// </summary>
    public void SetCaptureExclusion(bool exclude)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return; // window not yet initialized — OnSourceInitialized will call again
        SetWindowDisplayAffinity(hwnd, exclude ? WDA_EXCLUDEFROMCAPTURE : WDA_NONE);
    }

    private void ApplyCaptureExclusion()
    {
        var app = (App)Application.Current;
        SetCaptureExclusion(app.Settings.Current.ExcludeFromScreenCapture);
    }

    private void PositionInBottomRight()
    {
        var app = (App)Application.Current;
        var s = app.Settings.Current;

        // Restore last-known position when it falls on a connected screen
        if (s.OverlayLeft >= 0 && s.OverlayTop >= 0 &&
            IsPositionVisible(s.OverlayLeft, s.OverlayTop))
        {
            Left = s.OverlayLeft;
            Top  = s.OverlayTop;
            if (s.OverlayWidth  > 0) Width  = s.OverlayWidth;
            if (s.OverlayHeight > 0) Height = s.OverlayHeight;
            return;
        }

        // Default: bottom-right corner of primary screen
        var wa = System.Windows.Forms.Screen.PrimaryScreen?.WorkingArea
            ?? new System.Drawing.Rectangle(0, 0, 1920, 1080);
        Left = wa.Right  - Width  - 20;
        Top  = wa.Bottom - Height - 20;
    }

    // Check whether (left, top) falls within any connected screen's working area,
    // using a point 50px in from the corner so a partially-offscreen window still counts.
    private static bool IsPositionVisible(double left, double top)
    {
        var probe = new System.Drawing.Point((int)left + 50, (int)top + 20);
        return System.Windows.Forms.Screen.AllScreens.Any(s => s.WorkingArea.Contains(probe));
    }

    private void PersistWindowBounds()
    {
        var s = ((App)Application.Current).Settings.Current;
        s.OverlayLeft   = Left;
        s.OverlayTop    = Top;
        s.OverlayWidth  = ActualWidth;
        s.OverlayHeight = ActualHeight;
    }

    private void Header_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.ButtonState != System.Windows.Input.MouseButtonState.Pressed) return;
        _isDragging = true;
        _headerDragStart = PointToScreen(e.GetPosition(this));
        _dragStartLeft = Left;
        _dragStartTop  = Top;
        ((System.Windows.UIElement)sender).CaptureMouse();
        e.Handled = true;
    }

    private void Header_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!_isDragging) return;
        var pos = PointToScreen(e.GetPosition(this));
        Left = _dragStartLeft + (pos.X - _headerDragStart.X);
        Top  = _dragStartTop  + (pos.Y - _headerDragStart.Y);
    }

    private void Header_MouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (!_isDragging) return;
        _isDragging = false;
        ((System.Windows.UIElement)sender).ReleaseMouseCapture();
    }

    private void ResizeGrip_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        _isResizing = true;
        _resizeDragStart = PointToScreen(e.GetPosition(this));
        _resizeStartW = Width;
        _resizeStartH = Height;
        ((UIElement)sender).CaptureMouse();
        e.Handled = true;
    }

    private void ResizeGrip_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!_isResizing) return;
        var pos = PointToScreen(e.GetPosition(this));
        Width  = Math.Max(MinWidth,  _resizeStartW + (pos.X - _resizeDragStart.X));
        Height = Math.Max(MinHeight, _resizeStartH + (pos.Y - _resizeDragStart.Y));
    }

    private void ResizeGrip_MouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        _isResizing = false;
        ((UIElement)sender).ReleaseMouseCapture();
    }

    private void RecordToggle_Click(object sender, RoutedEventArgs e)
        => ((App)Application.Current).ToggleRecording();

    private void ScreenshotMode_Click(object sender, RoutedEventArgs e)
        => ((App)Application.Current).ToggleScreenshotMode();

    private void GoogleSearch_Click(object sender, RoutedEventArgs e)
        => ((App)Application.Current).ToggleGoogleSearchMode();

    private void CaptureSession_Click(object sender, RoutedEventArgs e)
        => ((App)Application.Current).HandleCaptureSessionButtonClick();

    private void RemoveShot_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button btn && btn.Tag is int index)
            ((OverlayViewModel)DataContext).RemoveCaptureSessionShot(index);
    }

    private void StopResponse_Click(object sender, RoutedEventArgs e)
        => ((App)Application.Current).StopResponse();

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        var win = new SettingsWindow();
        win.Owner = this;
        win.ShowDialog();
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Hide(); // hide to tray, don't close
    }

    private void HistoryPrev_Click(object sender, RoutedEventArgs e)
        => ((OverlayViewModel)DataContext).NavigateBack();

    private void HistoryNext_Click(object sender, RoutedEventArgs e)
        => ((OverlayViewModel)DataContext).NavigateForward();

    private void ExportTranscript_Click(object sender, RoutedEventArgs e)
        => ((App)Application.Current).ExportTranscript();

    private void CopyResponse_Click(object sender, RoutedEventArgs e)
    {
        var vm = (OverlayViewModel)DataContext;
        if (!string.IsNullOrEmpty(vm.ResponseText))
            System.Windows.Clipboard.SetText(vm.ResponseText);
    }

    private void ModeToggle_Click(object sender, RoutedEventArgs e)
        => ((App)Application.Current).CycleResponseMode();

    private void DismissDisclosure_Click(object sender, RoutedEventArgs e)
    {
        ((OverlayViewModel)DataContext).DismissDisclosureReminder();
        ((App)Application.Current).Settings.Update(s => s.ShowDisclosureReminder = false);
    }

    private void AudioSwitch_Click(object sender, RoutedEventArgs e)
        => ((App)Application.Current).SwitchToTeamsAudioDevice();

    private void AudioDismiss_Click(object sender, RoutedEventArgs e)
        => ((App)Application.Current).DismissAudioDeviceMismatch();
}

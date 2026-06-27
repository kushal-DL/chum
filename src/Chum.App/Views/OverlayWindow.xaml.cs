using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Chum.App.ViewModels;

namespace Chum.App.Views;

public partial class OverlayWindow : Window
{
    // Win32: prevent this window from appearing in any screen capture / recording / share frame.
    // WDA_EXCLUDEFROMCAPTURE requires Windows 10 2004+ (build 19041). Falls back silently on older builds.
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowDisplayAffinity(IntPtr hwnd, uint dwAffinity);
    private const uint WDA_NONE = 0x00000000;
    private const uint WDA_EXCLUDEFROMCAPTURE = 0x00000011;

    public int FontSize => (DataContext as OverlayViewModel) is not null ? 13 : 13;

    public OverlayWindow(OverlayViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;

        // Auto-scroll response area when new tokens arrive
        viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(OverlayViewModel.ResponseText))
                Dispatcher.InvokeAsync(() => ResponseScroller.ScrollToBottom());
        };
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
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
        var screen = System.Windows.Forms.Screen.PrimaryScreen?.WorkingArea
            ?? new System.Drawing.Rectangle(0, 0, 1920, 1080);
        Left = screen.Right - Width - 20;
        Top = screen.Bottom - Height - 20;
    }

    private void Header_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.ButtonState == System.Windows.Input.MouseButtonState.Pressed)
            DragMove();
    }

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
}

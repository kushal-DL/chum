using System.Windows;
using Chum.App.ViewModels;

namespace Chum.App.Views;

public partial class OverlayWindow : Window
{
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

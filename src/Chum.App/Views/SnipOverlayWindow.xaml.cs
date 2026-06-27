using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DrawingRect = System.Drawing.Rectangle;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MouseButtonEventArgs = System.Windows.Input.MouseButtonEventArgs;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;

namespace Chum.App.Views;

/// <summary>
/// Full-screen snip overlay. User drags a rectangle; on mouse release the selected
/// physical-pixel region is returned. ESC or a sub-10px selection returns null.
/// Call ShowAndGetSelectionAsync() — it shows the window and completes the task when done.
/// </summary>
public partial class SnipOverlayWindow : Window
{
    private readonly TaskCompletionSource<DrawingRect?> _tcs = new();
    private System.Windows.Point _startPoint;
    private bool _selecting;
    private double _dpiScaleX = 1.0;
    private double _dpiScaleY = 1.0;

    public SnipOverlayWindow()
    {
        InitializeComponent();
        // Cover the entire virtual screen (all monitors combined in WPF logical coordinates)
        Left   = SystemParameters.VirtualScreenLeft;
        Top    = SystemParameters.VirtualScreenTop;
        Width  = SystemParameters.VirtualScreenWidth;
        Height = SystemParameters.VirtualScreenHeight;
    }

    /// <summary>
    /// Shows the snip overlay and returns the selected physical-pixel region,
    /// or null if the user pressed ESC or made a too-small selection.
    /// </summary>
    public Task<DrawingRect?> ShowAndGetSelectionAsync()
    {
        Show();
        return _tcs.Task;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        // Capture per-window DPI scale to convert WPF logical coords → physical pixels
        var source = PresentationSource.FromVisual(this);
        if (source?.CompositionTarget is not null)
        {
            _dpiScaleX = source.CompositionTarget.TransformToDevice.M11;
            _dpiScaleY = source.CompositionTarget.TransformToDevice.M22;
        }
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
            Cancel();
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _startPoint = e.GetPosition(SelectionCanvas);
        _selecting  = true;
        HintText.Visibility       = Visibility.Collapsed;
        SelectionBorder.Visibility = Visibility.Visible;
        SelectionCanvas.CaptureMouse();
    }

    private void Window_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_selecting) return;
        UpdateSelectionRect(e.GetPosition(SelectionCanvas));
    }

    private void Window_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_selecting) return;
        _selecting = false;
        SelectionCanvas.ReleaseMouseCapture();

        var end  = e.GetPosition(SelectionCanvas);
        var logX = Math.Min(_startPoint.X, end.X);
        var logY = Math.Min(_startPoint.Y, end.Y);
        var logW = Math.Abs(end.X - _startPoint.X);
        var logH = Math.Abs(end.Y - _startPoint.Y);

        // Reject tiny accidental clicks
        if (logW < 10 || logH < 10)
        {
            Cancel();
            return;
        }

        // Convert WPF logical coords (relative to window) to physical screen pixels.
        // Window.Left/Top are in WPF logical coordinates = physical origin / DPI scale.
        var physX = (int)((Left + logX) * _dpiScaleX);
        var physY = (int)((Top  + logY) * _dpiScaleY);
        var physW = (int)(logW * _dpiScaleX);
        var physH = (int)(logH * _dpiScaleY);

        _tcs.TrySetResult(new DrawingRect(physX, physY, physW, physH));
        Close();
    }

    private void UpdateSelectionRect(System.Windows.Point current)
    {
        var x = Math.Min(_startPoint.X, current.X);
        var y = Math.Min(_startPoint.Y, current.Y);
        var w = Math.Abs(current.X - _startPoint.X);
        var h = Math.Abs(current.Y - _startPoint.Y);
        Canvas.SetLeft(SelectionBorder, x);
        Canvas.SetTop(SelectionBorder,  y);
        SelectionBorder.Width  = w;
        SelectionBorder.Height = h;
    }

    private void Cancel()
    {
        _tcs.TrySetResult(null);
        Close();
    }
}

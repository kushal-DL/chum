using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace Chum.App.Services;

// Listens for WM_CLIPBOARDUPDATE on a hidden message-only window and exposes clipboard image data.
// Must be created and disposed on the WPF UI thread (STA).
public sealed class ClipboardMonitor : IDisposable
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool AddClipboardFormatListener(IntPtr hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RemoveClipboardFormatListener(IntPtr hwnd);

    private const int WM_CLIPBOARDUPDATE = 0x031D;
    private static readonly IntPtr HWND_MESSAGE = new IntPtr(-3);

    private HwndSource? _source;

    public event EventHandler? ImageAvailable;
    public bool HasPendingImage { get; private set; }

    public ClipboardMonitor()
    {
        var p = new HwndSourceParameters("ChumClipboardMonitor")
        {
            ParentWindow = HWND_MESSAGE,
            WindowStyle = 0,
            ExtendedWindowStyle = 0
        };
        _source = new HwndSource(p);
        _source.AddHook(WndProc);
        AddClipboardFormatListener(_source.Handle);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_CLIPBOARDUPDATE)
        {
            if (System.Windows.Clipboard.ContainsImage())
            {
                HasPendingImage = true;
                ImageAvailable?.Invoke(this, EventArgs.Empty);
            }
            else
            {
                HasPendingImage = false;
            }
            handled = true;
        }
        return IntPtr.Zero;
    }

    // Encodes the current clipboard image as a JPEG and returns it as base64.
    // Clears the pending state. Returns null if the clipboard no longer has an image.
    // Must be called on the UI thread (STA).
    public string? TryTakeImageAsJpegBase64(int maxWidthPx = 1280, int jpegQuality = 85)
    {
        if (!System.Windows.Clipboard.ContainsImage()) return null;
        var bmp = System.Windows.Clipboard.GetImage();
        if (bmp is null) return null;

        HasPendingImage = false;
        return ImagePreprocessor.ToJpegBase64(bmp, maxWidthPx, jpegQuality);
    }

    public void Dispose()
    {
        if (_source is not null)
        {
            RemoveClipboardFormatListener(_source.Handle);
            _source.Dispose();
            _source = null;
        }
    }
}

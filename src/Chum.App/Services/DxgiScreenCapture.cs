using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using Serilog;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using D3D11MapFlags = Vortice.Direct3D11.MapFlags;

namespace Chum.App.Services;

/// <summary>
/// Captures the current desktop using DXGI Output Duplication.
///
/// Captures at the GPU output level — below DWM's WDA_EXCLUDEFROMCAPTURE filter for
/// non-Teams content. Teams call window video tiles still appear black because Microsoft
/// extended WDA_EXCLUDEFROMCAPTURE to cover DXGI in Windows 10 2004+. All other content
/// (slides, shared apps, whiteboards, other windows) is captured correctly.
/// </summary>
public sealed class DxgiScreenCapture : IDisposable
{
    private readonly ID3D11Device _device;
    private readonly ID3D11DeviceContext _context;
    private bool _disposed;

    private DxgiScreenCapture(ID3D11Device device, ID3D11DeviceContext context)
    {
        _device = device;
        _context = context;
    }

    /// <summary>
    /// Attempts to initialise the D3D11 device and verify DXGI duplication is available.
    /// Returns false in VMs, Remote Desktop sessions, or headless environments.
    /// </summary>
    public static bool TryCreate([NotNullWhen(true)] out DxgiScreenCapture? capture)
    {
        capture = null;
        try
        {
            var hr = D3D11.D3D11CreateDevice(
                null,
                DriverType.Hardware,
                DeviceCreationFlags.None,
                [FeatureLevel.Level_11_0, FeatureLevel.Level_10_1, FeatureLevel.Level_10_0],
                out var device,
                out _,
                out var context);

            if (hr.Failure || device is null || context is null)
            {
                Log.Warning("D3D11 device creation failed (hr={Hr}) — screen capture unavailable", hr.Code);
                return false;
            }

            // Verify duplication is possible on the primary output
            using var testDup = OpenPrimaryDuplication(device);
            testDup.Dispose();

            capture = new DxgiScreenCapture(device, context);
            return true;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "DXGI screen capture unavailable (VM, RDP, or unsupported GPU config)");
            return false;
        }
    }

    /// <summary>
    /// Captures the current primary display frame and returns a base64-encoded JPEG.
    /// Returns null if capture fails (e.g., no new frame within timeout).
    /// Teams call video tiles will appear black — inform the user via the overlay.
    /// </summary>
    public string? CaptureAsJpegBase64(int maxWidthPx = 1280, int jpegQuality = 85)
    {
        if (_disposed) return null;

        IDXGIOutputDuplication? duplication = null;
        IDXGIResource? resource = null;
        try
        {
            // Fresh duplication each call: first AcquireNextFrame returns the current desktop immediately
            duplication = OpenPrimaryDuplication(_device);
            var hr = duplication.AcquireNextFrame(500, out _, out resource);
            if (hr.Failure || resource is null)
            {
                Log.Debug("DXGI AcquireNextFrame returned {Hr} — no frame captured", hr.Code);
                return null;
            }

            using var srcTex = resource.QueryInterface<ID3D11Texture2D>();
            var desc = srcTex.Description;

            // Only handle the common BGRA8 desktop format; HDR formats are a future concern
            if (desc.Format != Format.B8G8R8A8_UNorm && desc.Format != Format.B8G8R8A8_UNorm_SRgb)
            {
                Log.Warning("DXGI frame format {Format} is not BGRA8 — screen capture skipped", desc.Format);
                return null;
            }

            // Copy to a CPU-readable staging texture
            using var staging = _device.CreateTexture2D(new Texture2DDescription
            {
                Width = desc.Width,
                Height = desc.Height,
                MipLevels = 1,
                ArraySize = 1,
                Format = desc.Format,
                SampleDescription = new SampleDescription(1, 0),
                Usage = ResourceUsage.Staging,
                BindFlags = BindFlags.None,
                CPUAccessFlags = CpuAccessFlags.Read,
                MiscFlags = ResourceOptionFlags.None,
            });

            _context.CopyResource(staging, srcTex);
            var mapped = _context.Map(staging, 0, MapMode.Read, D3D11MapFlags.None);
            try
            {
                return EncodeAsJpeg(mapped.DataPointer, desc.Width, desc.Height,
                    mapped.RowPitch, maxWidthPx, jpegQuality);
            }
            finally
            {
                _context.Unmap(staging, 0);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "DXGI screen capture failed");
            return null;
        }
        finally
        {
            resource?.Dispose();
            try { duplication?.ReleaseFrame(); } catch { /* suppress — frame may already be released on error */ }
            duplication?.Dispose();
        }
    }

    private static IDXGIOutputDuplication OpenPrimaryDuplication(ID3D11Device device)
    {
        using var dxgiDevice = device.QueryInterface<IDXGIDevice>();
        using var adapter = dxgiDevice.GetAdapter();
        adapter.EnumOutputs(0, out IDXGIOutput? primaryOutput).CheckError();
        using var output = primaryOutput!;
        using var output1 = output.QueryInterface<IDXGIOutput1>();
        return output1.DuplicateOutput(device);
    }

    private static string EncodeAsJpeg(nint dataPtr, int width, int height, int rowPitch, int maxWidth, int quality)
    {
        int targetWidth = Math.Min(width, maxWidth);
        int targetHeight = (int)Math.Round((double)height * targetWidth / width);
        int stride = width * 4; // 4 bytes per BGRA pixel

        // Copy pixel rows from DXGI (which may have row-pitch padding) into a flat buffer
        var pixels = new byte[height * stride];
        for (int row = 0; row < height; row++)
            Marshal.Copy(dataPtr + (nint)(row * rowPitch), pixels, row * stride, stride);

        using var bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        var bits = bmp.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try { Marshal.Copy(pixels, 0, bits.Scan0, pixels.Length); }
        finally { bmp.UnlockBits(bits); }

        // Resize if wider than maxWidthPx to keep base64 payload manageable for the LLM
        using Bitmap toEncode = targetWidth < width
            ? new Bitmap(bmp, new Size(targetWidth, targetHeight))
            : bmp;

        using var ms = new MemoryStream();
        var codec = ImageCodecInfo.GetImageEncoders().First(c => c.FormatID == ImageFormat.Jpeg.Guid);
        using var ep = new EncoderParameters(1);
        ep.Param[0] = new EncoderParameter(Encoder.Quality, (long)quality);
        toEncode.Save(ms, codec, ep);
        return Convert.ToBase64String(ms.ToArray());
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _context.Dispose();
        _device.Dispose();
    }
}

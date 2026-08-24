using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using System.Drawing.Imaging;
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
    ///
    /// On hybrid GPU systems (Optimus / iGPU+dGPU), D3D11CreateDevice with DriverType.Hardware
    /// picks the discrete GPU, which may have no outputs attached to the primary display.
    /// Output Duplication on the wrong adapter returns solid-black frames. We enumerate all
    /// DXGI adapters and create the device on the one whose output contains point (0,0).
    /// </summary>
    public static bool TryCreate([NotNullWhen(true)] out DxgiScreenCapture? capture)
    {
        capture = null;
        try
        {
            // Enumerate adapters to find the one driving the primary display
            DXGI.CreateDXGIFactory1(out IDXGIFactory1? factory).CheckError();
            using (factory)
            {
                for (int adapterIdx = 0; ; adapterIdx++)
                {
                    var enumHr = factory!.EnumAdapters1(adapterIdx, out IDXGIAdapter1? adapter);
                    if (enumHr.Failure || adapter is null) break;
                    using (adapter)
                    {
                        if (!AdapterHasPrimaryOutput(adapter)) continue;

                        var dhr = D3D11.D3D11CreateDevice(
                            adapter,
                            DriverType.Unknown, // must be Unknown when adapter is explicit
                            DeviceCreationFlags.None,
                            [FeatureLevel.Level_11_0, FeatureLevel.Level_10_1, FeatureLevel.Level_10_0],
                            out var device,
                            out _,
                            out var context);

                        if (dhr.Failure || device is null || context is null)
                        {
                            Log.Debug("D3D11 device creation failed on adapter {Idx}", adapterIdx);
                            continue;
                        }

                        try
                        {
                            using var testDup = OpenPrimaryDuplication(device);
                            testDup.Dispose();
                            capture = new DxgiScreenCapture(device, context);
                            Log.Information("DXGI screen capture ready (adapter {Idx})", adapterIdx);
                            return true;
                        }
                        catch
                        {
                            device.Dispose();
                            context?.Dispose();
                        }
                    }
                }
            }

            Log.Warning("No DXGI adapter with primary display output found — screen capture unavailable");
            return false;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "DXGI screen capture unavailable (VM, RDP, or unsupported GPU config)");
            return false;
        }
    }

    // Returns true if any output on this adapter has DesktopCoordinates containing (0,0).
    private static bool AdapterHasPrimaryOutput(IDXGIAdapter1 adapter)
    {
        for (int outIdx = 0; ; outIdx++)
        {
            var hr = adapter.EnumOutputs(outIdx, out IDXGIOutput? output);
            if (hr.Failure || output is null) return false;
            using (output)
            {
                var desc = output.Description;
                var r = desc.DesktopCoordinates;
                if (r.Left <= 0 && r.Top <= 0 && r.Right > 0 && r.Bottom > 0)
                    return true;
            }
        }
    }

    /// <summary>
    /// Captures the current primary display frame and returns a base64-encoded JPEG.
    /// Returns null if capture fails (e.g., no new frame within timeout).
    /// Teams call video tiles will appear black — inform the user via the overlay.
    /// </summary>
    public string? CaptureAsJpegBase64(int maxWidthPx = 1280, int jpegQuality = 85)
        => CaptureCore(null, maxWidthPx, jpegQuality);

    /// <summary>
    /// Captures only the specified physical-pixel region of the primary display.
    /// <paramref name="region"/> coordinates are physical screen pixels (not WPF logical units).
    /// Returns null on failure; empty string if the region falls outside the captured frame.
    /// </summary>
    public string? CaptureRegionAsJpegBase64(Rectangle region, int maxWidthPx = 1280, int jpegQuality = 85)
        => CaptureCore(region, maxWidthPx, jpegQuality);

    private string? CaptureCore(Rectangle? cropRegion, int maxWidthPx, int jpegQuality)
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
                    mapped.RowPitch, maxWidthPx, jpegQuality, cropRegion);
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

    private static string EncodeAsJpeg(nint dataPtr, int width, int height, int rowPitch,
        int maxWidth, int quality, Rectangle? cropRegion = null)
    {
        int stride = width * 4; // 4 bytes per BGRA pixel

        // Copy pixel rows from DXGI (which may have row-pitch padding) into a flat buffer
        var pixels = new byte[height * stride];
        for (int row = 0; row < height; row++)
            Marshal.Copy(dataPtr + (nint)(row * rowPitch), pixels, row * stride, stride);

        using var fullBmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        var bits = fullBmp.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try { Marshal.Copy(pixels, 0, bits.Scan0, pixels.Length); }
        finally { fullBmp.UnlockBits(bits); }

        if (cropRegion is null)
            return ImagePreprocessor.ToJpegBase64(fullBmp, maxWidth, quality);

        var safeRegion = Rectangle.Intersect(cropRegion.Value, new Rectangle(0, 0, width, height));
        if (safeRegion.IsEmpty) return string.Empty;
        using var cropped = fullBmp.Clone(safeRegion, PixelFormat.Format32bppArgb);
        return ImagePreprocessor.ToJpegBase64(cropped, maxWidth, quality);
    }

    /// <summary>
    /// GDI fallback capture for windows that appear black under DXGI Output Duplication
    /// (GPU-composited browsers, hardware-overlay windows, etc.).
    /// Slower than DXGI but sees whatever is visible on screen.
    /// </summary>
    public static string? CaptureRegionViaGdi(Rectangle region, int maxWidthPx = 1920, int jpegQuality = 90)
    {
        if (region.Width <= 0 || region.Height <= 0) return null;
        try
        {
            using var bmp = new Bitmap(region.Width, region.Height, PixelFormat.Format32bppArgb);
            using var g = Graphics.FromImage(bmp);
            g.CopyFromScreen(region.X, region.Y, 0, 0, region.Size, CopyPixelOperation.SourceCopy);
            return ImagePreprocessor.ToJpegBase64(bmp, maxWidthPx, jpegQuality);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "GDI CopyFromScreen failed");
            return null;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _context.Dispose();
        _device.Dispose();
    }
}

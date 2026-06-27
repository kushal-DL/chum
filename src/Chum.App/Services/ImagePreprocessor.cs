using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Chum.App.Services;

public static class ImagePreprocessor
{
    public const int DefaultMaxWidthPx = 1280;
    public const int DefaultJpegQuality = 85;

    // Resize (if wider than maxWidthPx) and encode a WPF BitmapSource as JPEG base64.
    // No EXIF metadata is written — encoder receives a plain frame with no metadata argument.
    public static string ToJpegBase64(
        BitmapSource source,
        int maxWidthPx = DefaultMaxWidthPx,
        int jpegQuality = DefaultJpegQuality)
    {
        BitmapSource toEncode = source.PixelWidth > maxWidthPx
            ? new TransformedBitmap(source, new ScaleTransform((double)maxWidthPx / source.PixelWidth, (double)maxWidthPx / source.PixelWidth))
            : source;

        var encoder = new JpegBitmapEncoder { QualityLevel = jpegQuality };
        encoder.Frames.Add(BitmapFrame.Create(toEncode));
        using var ms = new MemoryStream();
        encoder.Save(ms);
        return Convert.ToBase64String(ms.ToArray());
    }

    // Resize (if wider than maxWidthPx) and encode a GDI+ Bitmap as JPEG base64.
    // Does not dispose the incoming bitmap — caller owns its lifetime.
    public static string ToJpegBase64(
        Bitmap bmp,
        int maxWidthPx = DefaultMaxWidthPx,
        int jpegQuality = DefaultJpegQuality)
    {
        int targetWidth = Math.Min(bmp.Width, maxWidthPx);
        int targetHeight = (int)Math.Round((double)bmp.Height * targetWidth / bmp.Width);

        Bitmap? resized = targetWidth < bmp.Width
            ? new Bitmap(bmp, new Size(targetWidth, targetHeight))
            : null;
        try
        {
            var codec = ImageCodecInfo.GetImageEncoders().First(c => c.FormatID == ImageFormat.Jpeg.Guid);
            using var ep = new EncoderParameters(1);
            ep.Param[0] = new EncoderParameter(Encoder.Quality, (long)jpegQuality);
            using var ms = new MemoryStream();
            (resized ?? bmp).Save(ms, codec, ep);
            return Convert.ToBase64String(ms.ToArray());
        }
        finally
        {
            resized?.Dispose();
        }
    }
}

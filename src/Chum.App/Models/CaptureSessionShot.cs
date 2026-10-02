using System.Windows.Media.Imaging;

namespace Chum.App.Models;

/// <summary>One captured frame inside an active capture session.</summary>
public sealed record CaptureSessionShot(int Index, string ImageBase64, BitmapSource Thumbnail);

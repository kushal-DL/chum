namespace Chum.Audio.Capture;

public interface IAudioCapture : IDisposable
{
    /// <summary>Fires for each raw audio buffer from the device (device native format).</summary>
    event EventHandler<RawAudioEventArgs>? RawAudioAvailable;

    string DeviceName { get; }
    bool IsCapturing { get; }

    void Start();
    void Stop();
}

public sealed class RawAudioEventArgs(byte[] buffer, int bytesRecorded, NAudio.Wave.WaveFormat format) : EventArgs
{
    public byte[] Buffer { get; } = buffer;
    public int BytesRecorded { get; } = bytesRecorded;
    public NAudio.Wave.WaveFormat Format { get; } = format;
}

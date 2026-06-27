using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace Chum.Audio.Capture;

/// <summary>
/// Captures system audio (everything playing through the output device) via WASAPI loopback.
/// Works with all meeting apps — Teams, Google Meet, Zoom — as long as they use the
/// selected output device (follow device if Teams uses a non-default device).
/// </summary>
public sealed class LoopbackCapture : IAudioCapture
{
    private WasapiLoopbackCapture? _capture;
    private readonly string? _deviceId; // null = Windows default

    public event EventHandler<RawAudioEventArgs>? RawAudioAvailable;

    public string DeviceName { get; private set; } = "Default Output";
    public bool IsCapturing { get; private set; }

    public LoopbackCapture(string? deviceId = null)
    {
        _deviceId = deviceId;
    }

    public void Start()
    {
        if (IsCapturing) return;

        MMDevice device;
        var enumerator = new MMDeviceEnumerator();
        if (_deviceId is not null)
        {
            device = enumerator.GetDevice(_deviceId);
        }
        else
        {
            device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        }

        DeviceName = device.FriendlyName;
        _capture = new WasapiLoopbackCapture(device);
        _capture.DataAvailable += OnDataAvailable;
        _capture.RecordingStopped += OnRecordingStopped;
        _capture.StartRecording();
        IsCapturing = true;
    }

    public void Stop()
    {
        if (!IsCapturing) return;
        _capture?.StopRecording();
        IsCapturing = false;
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        if (e.BytesRecorded == 0 || _capture is null) return;

        var buffer = new byte[e.BytesRecorded];
        Buffer.BlockCopy(e.Buffer, 0, buffer, 0, e.BytesRecorded);
        RawAudioAvailable?.Invoke(this, new RawAudioEventArgs(buffer, e.BytesRecorded, _capture.WaveFormat));
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        IsCapturing = false;
        if (e.Exception is not null)
            Serilog.Log.Error(e.Exception, "Loopback capture stopped with error");
    }

    public void Dispose()
    {
        Stop();
        _capture?.Dispose();
    }
}

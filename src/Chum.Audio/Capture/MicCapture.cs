using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace Chum.Audio.Capture;

/// <summary>
/// Captures microphone input via WASAPI shared mode.
/// This is a separate WASAPI session from Teams/Meet — it does NOT affect the
/// mute state seen by meeting participants.
/// </summary>
public sealed class MicCapture : IAudioCapture
{
    private WasapiCapture? _capture;
    private readonly string? _deviceId;

    public event EventHandler<RawAudioEventArgs>? RawAudioAvailable;

    public string DeviceName { get; private set; } = "Default Microphone";
    public bool IsCapturing { get; private set; }

    public MicCapture(string? deviceId = null)
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
            device = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);
        }

        DeviceName = device.FriendlyName;

        // Warn if mic sample rate is too low (Bluetooth HFP mode = 8kHz)
        _capture = new WasapiCapture(device);
        if (_capture.WaveFormat.SampleRate < 16000)
            Serilog.Log.Warning("Microphone sample rate {Rate}Hz is below 16kHz — transcription quality may be poor. Use a USB or 3.5mm headset for best results.", _capture.WaveFormat.SampleRate);

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
            Serilog.Log.Error(e.Exception, "Mic capture stopped with error");
    }

    public void Dispose()
    {
        Stop();
        _capture?.Dispose();
    }
}

using System.Threading.Channels;
using Chum.Audio.Capture;
using Chum.Audio.Models;
using Chum.Audio.Vad;

namespace Chum.Audio.Pipeline;

/// <summary>
/// Orchestrates loopback + mic capture → format conversion → VAD → speech segment assembly.
/// Outputs complete speech segments to the transcription channel.
///
/// VAD logic:
///   - 300 ms pre-buffer prepended to avoid clipping word starts
///   - 600 ms post-buffer silence allowed before segment is flushed
///   - Segment capped at 25 s to prevent Whisper hallucinations on very long inputs
/// </summary>
public sealed class AudioPipeline : IDisposable
{
    private const int PreBufferMs = 300;
    private const int PostSilenceMs = 400;   // was 600 — flush sooner after speech ends
    private const int MaxSegmentMs = 5_000;  // GPU inference ~300ms per clip → ~5.3s worst-case latency
    private const int SampleRate = 16_000;

    private readonly IAudioCapture _loopback;
    private readonly IAudioCapture _mic;
    private readonly IVad _loopbackVad;
    private readonly IVad _micVad;
    private readonly bool _noiseSuppress;

    // Press-to-record raw capture: accumulates ALL audio during the recording window,
    // independent of VAD. Mic and loopback are kept separate so they can be mixed on stop.
    private readonly object _rawRecLock = new();
    private List<float>? _rawMic;
    private List<float>? _rawLoop;

    // Pre-buffer: ring of recent raw chunks used to prepend to a new speech segment
    private readonly Queue<(float[] samples, AudioSource src)> _preBuffer = new();
    private int _preBufferSamples;
    private readonly int _maxPreBufferSamples;

    // Active speech accumulation
    private readonly List<float[]> _currentSegment = [];
    private AudioSource _currentSource;
    private bool _inSpeech;
    private int _silenceSamples;
    private int _segmentSamples;
    private readonly int _postSilenceSamples;
    private readonly int _maxSegmentSamples;

    private readonly Channel<AudioChunk> _outputChannel;
    public ChannelReader<AudioChunk> Output => _outputChannel.Reader;

    /// <summary>Fires when either capture device disconnects unexpectedly. At most once per pipeline instance.</summary>
    public event EventHandler? CaptureDisconnected;

    /// <summary>Fires for every raw audio chunk after VAD classification. Rate ≈ WASAPI callback rate (~20–100 Hz).</summary>
    public event EventHandler<AudioLevelEventArgs>? LevelChanged;

    private bool _paused;
    private bool _disposed;
    private int _disconnectFired; // interlocked flag — ensures CaptureDisconnected fires at most once

    public AudioPipeline(IAudioCapture loopback, IAudioCapture mic,
        IVad? loopbackVad = null, IVad? micVad = null,
        bool enableNoiseSuppression = false, int outputChannelCapacity = 64)
    {
        _loopback = loopback;
        _mic = mic;
        _loopbackVad = loopbackVad ?? new EnergyVad();
        _micVad = micVad ?? new EnergyVad();
        _noiseSuppress = enableNoiseSuppression;
        _maxPreBufferSamples = SampleRate * PreBufferMs / 1000;
        _postSilenceSamples = SampleRate * PostSilenceMs / 1000;
        _maxSegmentSamples = SampleRate * MaxSegmentMs / 1000;
        _outputChannel = Channel.CreateBounded<AudioChunk>(
            new BoundedChannelOptions(outputChannelCapacity) { FullMode = BoundedChannelFullMode.DropOldest });

        _loopback.RawAudioAvailable += (_, e) => ProcessRaw(e, AudioSource.Loopback);
        _mic.RawAudioAvailable += (_, e) => ProcessRaw(e, AudioSource.Microphone);
        _loopback.Disconnected += OnCaptureDisconnected;
        _mic.Disconnected += OnCaptureDisconnected;
    }

    public void Start()
    {
        _loopback.Start();
        _mic.Start();
    }

    public void Stop()
    {
        _loopback.Stop();
        _mic.Stop();
    }

    public void Pause() => _paused = true;
    public void Resume() => _paused = false;

    /// <summary>Begin accumulating raw audio (mic + loopback) for a press-to-record query, bypassing VAD.</summary>
    public void StartRawRecording()
    {
        lock (_rawRecLock)
        {
            _rawMic = new List<float>(16_000 * 30);
            _rawLoop = new List<float>(16_000 * 30);
        }
    }

    /// <summary>
    /// Stop raw recording and return the captured audio as a single mono mix of mic + loopback,
    /// noise-suppressed if enabled. Returns null if no recording was active.
    /// </summary>
    public float[]? StopRawRecording()
    {
        float[]? mic, loop;
        lock (_rawRecLock)
        {
            mic = _rawMic?.ToArray();
            loop = _rawLoop?.ToArray();
            _rawMic = null;
            _rawLoop = null;
        }

        if (mic is null && loop is null) return null;
        int n = Math.Max(mic?.Length ?? 0, loop?.Length ?? 0);
        if (n == 0) return [];

        // Both streams are continuous 16 kHz from StartRawRecording, so index-aligned summing is a
        // good approximation of a time-aligned mix (no sample-accurate clock, but close enough for STT).
        var mixed = new float[n];
        for (int i = 0; i < n; i++)
        {
            float a = mic is not null && i < mic.Length ? mic[i] : 0f;
            float b = loop is not null && i < loop.Length ? loop[i] : 0f;
            mixed[i] = Math.Clamp(a + b, -1f, 1f);
        }

        return _noiseSuppress ? NoiseSuppressor.Process(mixed) : mixed;
    }

    private void OnCaptureDisconnected(object? sender, EventArgs e)
    {
        // Interlocked ensures only one disconnect notification fires even if both devices fail simultaneously
        if (Interlocked.Exchange(ref _disconnectFired, 1) == 0)
            CaptureDisconnected?.Invoke(this, EventArgs.Empty);
    }

    private static float ComputeRms(float[] samples)
    {
        if (samples.Length == 0) return 0f;
        double sum = 0;
        foreach (var s in samples) sum += s * s;
        return (float)Math.Sqrt(sum / samples.Length);
    }

    private void ProcessRaw(RawAudioEventArgs e, AudioSource source)
    {
        if (_paused || _disposed) return;

        float[] samples = AudioConverter.ToMono16kHz(e.Buffer, e.BytesRecorded, e.Format);
        if (samples.Length == 0) return;

        // Press-to-record tap: capture everything, regardless of VAD, into the per-source buffer.
        if (_rawMic is not null || _rawLoop is not null)
        {
            lock (_rawRecLock)
            {
                if (source == AudioSource.Loopback) _rawLoop?.AddRange(samples);
                else _rawMic?.AddRange(samples);
            }
        }

        var vad = source == AudioSource.Loopback ? _loopbackVad : _micVad;
        // Run VAD on noise-suppressed audio so fan/HVAC noise doesn't trigger false positives.
        // The original samples are still accumulated in the segment buffer for Whisper quality.
        var vadInput = _noiseSuppress ? NoiseSuppressor.Process(samples) : samples;
        bool speech = vad.IsSpeech(vadInput);

        // Fire level event before entering the lock — pure read on local array, no shared state
        var rms = ComputeRms(samples);
        var dbFs = rms > 1e-7f ? 20f * MathF.Log10(rms) : -60f;
        LevelChanged?.Invoke(this, new AudioLevelEventArgs(source, MathF.Max(dbFs, -60f), speech));

        // Thread-safety: AudioPipeline state is touched from two capture threads (loopback + mic).
        // For the MVP we accept that occasional interleaving of short chunks is benign.
        // A lock here would be safer; added as a future improvement (perf penalty is low).
        lock (this)
        {
            UpdatePreBuffer(samples, source);

            if (!_inSpeech && speech)
                StartSegment(samples, source);
            else if (_inSpeech && speech)
                ContinueSegment(samples);
            else if (_inSpeech && !speech)
                AccumulateSilence(samples);
        }
    }

    private void UpdatePreBuffer(float[] samples, AudioSource src)
    {
        _preBuffer.Enqueue((samples, src));
        _preBufferSamples += samples.Length;
        while (_preBufferSamples > _maxPreBufferSamples && _preBuffer.Count > 0)
        {
            var removed = _preBuffer.Dequeue();
            _preBufferSamples -= removed.samples.Length;
        }
    }

    private void StartSegment(float[] samples, AudioSource source)
    {
        _inSpeech = true;
        _silenceSamples = 0;
        _currentSource = source;
        _currentSegment.Clear();
        _segmentSamples = 0;

        // Prepend pre-buffer so word starts are not clipped
        foreach (var (preChunk, _) in _preBuffer)
        {
            _currentSegment.Add(preChunk);
            _segmentSamples += preChunk.Length;
        }
        _currentSegment.Add(samples);
        _segmentSamples += samples.Length;
    }

    private void ContinueSegment(float[] samples)
    {
        _silenceSamples = 0;
        _currentSegment.Add(samples);
        _segmentSamples += samples.Length;

        if (_segmentSamples >= _maxSegmentSamples)
            FlushSegment();
    }

    private void AccumulateSilence(float[] samples)
    {
        _currentSegment.Add(samples);
        _segmentSamples += samples.Length;
        _silenceSamples += samples.Length;

        if (_silenceSamples >= _postSilenceSamples)
            FlushSegment();
    }

    private void FlushSegment()
    {
        if (_currentSegment.Count == 0) { _inSpeech = false; return; }

        int total = _currentSegment.Sum(c => c.Length);
        float[] merged = new float[total];
        int offset = 0;
        foreach (var chunk in _currentSegment)
        {
            chunk.CopyTo(merged, offset);
            offset += chunk.Length;
        }

        // Zero out sensitive audio bytes after handing off (privacy)
        foreach (var chunk in _currentSegment) Array.Clear(chunk);

        _currentSegment.Clear();
        _segmentSamples = 0;
        _silenceSamples = 0;
        _inSpeech = false;

        if (_noiseSuppress) merged = NoiseSuppressor.Process(merged);

        var audioChunk = new AudioChunk(merged, _currentSource, DateTimeOffset.UtcNow);
        _outputChannel.Writer.TryWrite(audioChunk);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
        _outputChannel.Writer.TryComplete();
        _loopback.Dispose();
        _mic.Dispose();
        (_loopbackVad as IDisposable)?.Dispose();
        (_micVad as IDisposable)?.Dispose();
    }
}

/// <summary>Level data fired by <see cref="AudioPipeline.LevelChanged"/> on each audio callback.</summary>
public sealed record AudioLevelEventArgs(AudioSource Source, float LevelDbFs, bool IsSpeech);

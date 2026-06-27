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
    private const int PostSilenceMs = 600;
    private const int MaxSegmentMs = 25_000;
    private const int SampleRate = 16_000;

    private readonly IAudioCapture _loopback;
    private readonly IAudioCapture _mic;
    private readonly IVad _loopbackVad;
    private readonly IVad _micVad;

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

    private bool _paused;
    private bool _disposed;

    public AudioPipeline(IAudioCapture loopback, IAudioCapture mic,
        IVad? loopbackVad = null, IVad? micVad = null, int outputChannelCapacity = 64)
    {
        _loopback = loopback;
        _mic = mic;
        _loopbackVad = loopbackVad ?? new EnergyVad();
        _micVad = micVad ?? new EnergyVad();
        _maxPreBufferSamples = SampleRate * PreBufferMs / 1000;
        _postSilenceSamples = SampleRate * PostSilenceMs / 1000;
        _maxSegmentSamples = SampleRate * MaxSegmentMs / 1000;
        _outputChannel = Channel.CreateBounded<AudioChunk>(
            new BoundedChannelOptions(outputChannelCapacity) { FullMode = BoundedChannelFullMode.DropOldest });

        _loopback.RawAudioAvailable += (_, e) => ProcessRaw(e, AudioSource.Loopback);
        _mic.RawAudioAvailable += (_, e) => ProcessRaw(e, AudioSource.Microphone);
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

    private void ProcessRaw(RawAudioEventArgs e, AudioSource source)
    {
        if (_paused || _disposed) return;

        float[] samples = AudioConverter.ToMono16kHz(e.Buffer, e.BytesRecorded, e.Format);
        if (samples.Length == 0) return;

        var vad = source == AudioSource.Loopback ? _loopbackVad : _micVad;
        bool speech = vad.IsSpeech(samples);

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

namespace Chum.Audio.Vad;

/// <summary>
/// Simple RMS energy-based Voice Activity Detector.
/// Accurate enough for clean office / home-office audio.
/// Replace with SileroVad (ONNX) for noisy environments — same interface, swap in AudioPipeline.
/// </summary>
public sealed class EnergyVad
{
    private readonly float _onThreshold;   // RMS threshold to enter speech
    private readonly float _offThreshold;  // RMS threshold to exit speech (hysteresis)
    private bool _isSpeaking;

    /// <param name="onThresholdDb">dBFS to start speech detection (default -40 dBFS)</param>
    /// <param name="offThresholdDb">dBFS to end speech detection (default -45 dBFS)</param>
    public EnergyVad(float onThresholdDb = -40f, float offThresholdDb = -45f)
    {
        _onThreshold = DbToLinear(onThresholdDb);
        _offThreshold = DbToLinear(offThresholdDb);
    }

    /// <returns>True if this chunk contains speech.</returns>
    public bool IsSpeech(ReadOnlySpan<float> samples)
    {
        if (samples.IsEmpty) return false;

        float sumSq = 0f;
        foreach (var s in samples) sumSq += s * s;
        float rms = MathF.Sqrt(sumSq / samples.Length);

        if (!_isSpeaking && rms > _onThreshold)
            _isSpeaking = true;
        else if (_isSpeaking && rms < _offThreshold)
            _isSpeaking = false;

        return _isSpeaking;
    }

    private static float DbToLinear(float db) => MathF.Pow(10f, db / 20f);
}

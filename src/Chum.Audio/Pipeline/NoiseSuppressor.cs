namespace Chum.Audio.Pipeline;

/// <summary>
/// Lightweight time-domain noise reduction for 16 kHz mono speech:
///   1. One-pole high-pass removes DC offset and low-frequency rumble (fans, HVAC, desk thumps).
///   2. An adaptive noise gate estimates the room noise floor and attenuates frames near it,
///      with fast-attack / slow-release smoothing so speech onsets are not clipped.
///
/// This is not a neural denoiser (RNNoise/GTCRN) but it is real, allocation-light, and fully
/// unit-testable — enough to stop steady background noise from reaching STT/LLM in a home office.
/// </summary>
public static class NoiseSuppressor
{
    private const int FrameSize = 160; // 10 ms at 16 kHz

    /// <param name="input">Mono float samples (-1..1).</param>
    /// <param name="gateMarginDb">dB above the noise floor a frame must reach to pass (default 6 dB).</param>
    /// <param name="floorGain">Residual gain applied to gated noise frames (0.1 = -20 dB).</param>
    public static float[] Process(ReadOnlySpan<float> input,
        float gateMarginDb = 6f, float floorGain = 0.1f)
    {
        if (input.Length == 0) return [];

        // 1. One-pole high-pass (cutoff ~80 Hz at 16 kHz). y[n] = a*(y[n-1] + x[n] - x[n-1])
        const float a = 0.97f;
        var hp = new float[input.Length];
        float prevIn = 0f, prevOut = 0f;
        for (int i = 0; i < input.Length; i++)
        {
            float x = input[i];
            float y = a * (prevOut + x - prevIn);
            hp[i] = y;
            prevIn = x;
            prevOut = y;
        }

        int frames = hp.Length / FrameSize;
        if (frames < 4) return hp; // too short to estimate a floor — just return the high-passed signal

        // 2. Per-frame RMS, then noise floor = 20th percentile (robust to speech-heavy clips)
        var rms = new float[frames];
        for (int f = 0; f < frames; f++)
        {
            int off = f * FrameSize;
            double sum = 0;
            for (int j = 0; j < FrameSize; j++)
            {
                float s = hp[off + j];
                sum += s * (double)s;
            }
            rms[f] = (float)Math.Sqrt(sum / FrameSize);
        }
        var sorted = (float[])rms.Clone();
        Array.Sort(sorted);
        float noiseFloor = sorted[(int)(frames * 0.1)];
        float threshold = noiseFloor * (float)Math.Pow(10, gateMarginDb / 20.0);

        // Absolute "definitely speech" level (~-34 dBFS). A frame this loud always passes, so a clip
        // that is mostly loud (floor estimate lands in speech) is never gated to silence.
        const float absSpeech = 0.02f;

        // 3. Gate with smoothing. Fast attack toward open (1.0), slow release toward floorGain.
        const float attack = 0.4f, release = 0.05f;
        var output = new float[hp.Length];
        float gain = 1f;
        for (int f = 0; f < frames; f++)
        {
            float target = rms[f] >= threshold || rms[f] >= absSpeech ? 1f : floorGain;
            int off = f * FrameSize;
            for (int j = 0; j < FrameSize; j++)
            {
                float coef = target > gain ? attack : release;
                gain += coef * (target - gain);
                output[off + j] = hp[off + j] * gain;
            }
        }
        for (int i = frames * FrameSize; i < hp.Length; i++)
            output[i] = hp[i] * gain;

        return output;
    }
}

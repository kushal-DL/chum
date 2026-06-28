namespace Chum.Transcription;

/// <summary>
/// Computes the log-mel spectrogram exactly as openai/whisper expects it.
/// Matches whisper/audio.py: log_mel_spectrogram() with center=False STFT padding.
/// Output shape: [N_MELS * N_FRAMES] = [80 * 3000] flat float array, row-major [mel, frame].
/// </summary>
internal static class MelSpectrogram
{
    public const int N_FFT = 400;
    public const int HOP_LENGTH = 160;
    public const int N_MELS = 80;
    public const int SAMPLE_RATE = 16_000;
    public const int N_SAMPLES = 480_000; // 30 s × 16 kHz
    public const int N_FRAMES = 3_000;    // N_SAMPLES / HOP_LENGTH
    private const int FFT_SIZE = 512;     // next power-of-2 ≥ N_FFT
    private const int N_FREQS = N_FFT / 2 + 1; // 201

    private static readonly float[] _hann = BuildHann();
    private static readonly float[,] _melFilters = BuildMelFilterbank();

    /// <summary>
    /// Converts raw 16 kHz mono PCM to Whisper's log-mel spectrogram.
    /// Audio shorter than 30 s is zero-padded; longer audio is truncated.
    /// </summary>
    public static float[] Compute(float[] audio)
    {
        // Zero-pad to N_SAMPLES + N_FFT/2 so the last window doesn't read past the signal
        int padLen = N_SAMPLES + N_FFT / 2;
        float[] padded = new float[padLen];
        int copyLen = Math.Min(audio.Length, N_SAMPLES);
        audio.AsSpan(0, copyLen).CopyTo(padded.AsSpan());

        // Per-frame power spectrum [N_FREQS × N_FRAMES], stored [freq, frame]
        float[] energies = new float[N_FREQS * N_FRAMES];
        (float re, float im)[] fftBuf = new (float, float)[FFT_SIZE];

        for (int t = 0; t < N_FRAMES; t++)
        {
            int offset = t * HOP_LENGTH;

            // Apply Hann window and load into FFT buffer (zero-pad N_FFT→FFT_SIZE automatically)
            for (int i = 0; i < FFT_SIZE; i++)
                fftBuf[i] = (0f, 0f);
            for (int i = 0; i < N_FFT; i++)
                fftBuf[i] = (padded[offset + i] * _hann[i], 0f);

            Fft(fftBuf);

            // Power spectrum: only first N_FREQS = 201 bins
            int tBase = t; // frame index; freq index strides by N_FRAMES
            for (int f = 0; f < N_FREQS; f++)
            {
                float re = fftBuf[f].re, im = fftBuf[f].im;
                energies[f * N_FRAMES + tBase] = re * re + im * im;
            }
        }

        // Apply mel filterbank → log10 → normalize
        float[] mel = new float[N_MELS * N_FRAMES];
        float globalMax = float.NegativeInfinity;

        for (int m = 0; m < N_MELS; m++)
        {
            int mBase = m * N_FRAMES;
            for (int t = 0; t < N_FRAMES; t++)
            {
                float sum = 0f;
                for (int f = 0; f < N_FREQS; f++)
                    sum += _melFilters[m, f] * energies[f * N_FRAMES + t];

                float logVal = MathF.Log10(MathF.Max(sum, 1e-10f));
                mel[mBase + t] = logVal;
                if (logVal > globalMax) globalMax = logVal;
            }
        }

        // Clamp to [max-8, max], then shift+scale to approximate [-1, 1]
        float floor = globalMax - 8f;
        for (int i = 0; i < mel.Length; i++)
            mel[i] = (MathF.Max(mel[i], floor) + 4f) / 4f;

        return mel;
    }

    // ── Hann window ───────────────────────────────────────────────────────────

    private static float[] BuildHann()
    {
        var w = new float[N_FFT];
        for (int i = 0; i < N_FFT; i++)
            w[i] = 0.5f * (1f - MathF.Cos(2f * MathF.PI * i / N_FFT));
        return w;
    }

    // ── Mel filterbank ────────────────────────────────────────────────────────

    private static float[,] BuildMelFilterbank()
    {
        const float fMin = 0f;
        const float fMax = 8000f; // Nyquist for 16 kHz

        float melMin = HzToMel(fMin);
        float melMax = HzToMel(fMax);

        // N_MELS + 2 equally spaced mel-scale points
        float[] melPts = new float[N_MELS + 2];
        for (int i = 0; i < N_MELS + 2; i++)
            melPts[i] = melMin + (melMax - melMin) * i / (N_MELS + 1);

        float[] hzPts = new float[N_MELS + 2];
        for (int i = 0; i < N_MELS + 2; i++)
            hzPts[i] = MelToHz(melPts[i]);

        // FFT bin centre frequencies (using N_FFT, not FFT_SIZE, for frequency resolution)
        float[] binHz = new float[N_FREQS];
        for (int f = 0; f < N_FREQS; f++)
            binHz[f] = (float)f * SAMPLE_RATE / N_FFT;

        var filters = new float[N_MELS, N_FREQS];
        for (int m = 0; m < N_MELS; m++)
        {
            float lo = hzPts[m], center = hzPts[m + 1], hi = hzPts[m + 2];
            for (int f = 0; f < N_FREQS; f++)
            {
                float hz = binHz[f];
                if (hz >= lo && hz <= center)
                    filters[m, f] = (hz - lo) / (center - lo);
                else if (hz > center && hz <= hi)
                    filters[m, f] = (hi - hz) / (hi - center);
            }
        }
        return filters;
    }

    private static float HzToMel(float hz) => 2595f * MathF.Log10(1f + hz / 700f);
    private static float MelToHz(float mel) => 700f * (MathF.Pow(10f, mel / 2595f) - 1f);

    // ── Cooley-Tukey in-place FFT (power-of-2 sizes) ─────────────────────────

    private static void Fft(Span<(float re, float im)> data)
    {
        int n = data.Length;

        // Bit-reversal permutation
        for (int i = 1, j = 0; i < n; i++)
        {
            int bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1) j ^= bit;
            j ^= bit;
            if (i < j) (data[i], data[j]) = (data[j], data[i]);
        }

        // Butterfly stages
        for (int len = 2; len <= n; len <<= 1)
        {
            double ang = -2 * Math.PI / len;
            float wRe = (float)Math.Cos(ang);
            float wIm = (float)Math.Sin(ang);

            for (int i = 0; i < n; i += len)
            {
                float cRe = 1f, cIm = 0f;
                for (int j = 0; j < len >> 1; j++)
                {
                    var (uRe, uIm) = data[i + j];
                    var (vRe, vIm) = data[i + j + (len >> 1)];
                    float tRe = cRe * vRe - cIm * vIm;
                    float tIm = cRe * vIm + cIm * vRe;
                    data[i + j]           = (uRe + tRe, uIm + tIm);
                    data[i + j + (len >> 1)] = (uRe - tRe, uIm - tIm);
                    (cRe, cIm) = (cRe * wRe - cIm * wIm, cRe * wIm + cIm * wRe);
                }
            }
        }
    }
}

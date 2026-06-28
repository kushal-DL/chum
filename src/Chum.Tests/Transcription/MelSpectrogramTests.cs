using Chum.Transcription;
using Xunit;

namespace Chum.Tests.Transcription;

/// <summary>
/// Regression tests for MelSpectrogram.Compute.
///
/// Critical regression: the original padLen = N_SAMPLES + N_FFT/2 (480200) was 40 bytes short.
/// Frame 2999 reads padded[479840 + 399] = padded[480239] which is >= 480200 → IndexOutOfRangeException
/// on EVERY audio segment. Fix: padLen = N_SAMPLES + N_FFT (480400).
/// </summary>
public sealed class MelSpectrogramTests
{
    // ── No-throw regression (the bug this test suite was written to catch) ───────

    [Fact]
    public void Compute_Silence_DoesNotThrow()
    {
        // 30 s of silence — exercises every frame including the last (t=2999) that
        // previously caused IndexOutOfRangeException when padLen was N_SAMPLES + N_FFT/2.
        var audio = new float[MelSpectrogram.N_SAMPLES];
        var ex = Record.Exception(() => MelSpectrogram.Compute(audio));
        Assert.Null(ex);
    }

    [Fact]
    public void Compute_ShortAudio_DoesNotThrow()
    {
        // 1 s of audio — tests zero-padding path
        var audio = new float[MelSpectrogram.SAMPLE_RATE];
        var ex = Record.Exception(() => MelSpectrogram.Compute(audio));
        Assert.Null(ex);
    }

    [Fact]
    public void Compute_LongerThan30s_DoesNotThrow()
    {
        // Audio longer than 30 s must be truncated, not cause an out-of-bounds read
        var audio = new float[MelSpectrogram.N_SAMPLES + 16_000];
        var ex = Record.Exception(() => MelSpectrogram.Compute(audio));
        Assert.Null(ex);
    }

    [Fact]
    public void Compute_Empty_DoesNotThrow()
    {
        var ex = Record.Exception(() => MelSpectrogram.Compute([]));
        Assert.Null(ex);
    }

    // ── Output shape ──────────────────────────────────────────────────────────────

    [Fact]
    public void Compute_OutputShape_Is80x3000Flat()
    {
        var audio = new float[MelSpectrogram.N_SAMPLES];
        var mel = MelSpectrogram.Compute(audio);
        Assert.Equal(MelSpectrogram.N_MELS * MelSpectrogram.N_FRAMES, mel.Length);
    }

    // ── Output values ─────────────────────────────────────────────────────────────

    [Fact]
    public void Compute_Silence_AllValuesFinite()
    {
        var audio = new float[MelSpectrogram.N_SAMPLES];
        var mel = MelSpectrogram.Compute(audio);
        foreach (var v in mel)
            Assert.True(float.IsFinite(v), $"Non-finite value in mel spectrogram: {v}");
    }

    [Fact]
    public void Compute_Silence_AllValuesInNormalisedRange()
    {
        // After (max - 8 clamp) + normalisation the output is in roughly [-1, 1].
        // Silence produces uniform energy so all bins should be at the floor (≈ -1.0).
        var audio = new float[MelSpectrogram.N_SAMPLES];
        var mel = MelSpectrogram.Compute(audio);
        foreach (var v in mel)
        {
            Assert.True(v >= -2.0f && v <= 2.0f,
                $"Value {v} is outside the expected normalised range [-2, 2]");
        }
    }

    [Fact]
    public void Compute_TonePlusZero_DifferentFromSilence()
    {
        // A 440 Hz sine should produce a different (higher-energy) mel than silence
        var silence = new float[MelSpectrogram.N_SAMPLES];
        var tone    = new float[MelSpectrogram.N_SAMPLES];
        for (int i = 0; i < tone.Length; i++)
            tone[i] = 0.5f * MathF.Sin(2 * MathF.PI * 440f * i / MelSpectrogram.SAMPLE_RATE);

        var silMel  = MelSpectrogram.Compute(silence);
        var toneMel = MelSpectrogram.Compute(tone);

        float silMax  = silMel.Max();
        float toneMax = toneMel.Max();
        Assert.True(toneMax > silMax, "Tone should produce higher mel energy than silence");
    }

    // ── Constants sanity ─────────────────────────────────────────────────────────

    [Fact]
    public void Constants_PaddedLengthEnoughForLastFrame()
    {
        // The last frame (t = N_FRAMES - 1) reads up to offset + N_FFT - 1.
        // This must be < N_SAMPLES + N_FFT (the current padLen).
        int lastFrameMaxIndex = (MelSpectrogram.N_FRAMES - 1) * MelSpectrogram.HOP_LENGTH
                                + MelSpectrogram.N_FFT - 1;
        int padLen = MelSpectrogram.N_SAMPLES + MelSpectrogram.N_FFT;
        Assert.True(lastFrameMaxIndex < padLen,
            $"Last frame would read index {lastFrameMaxIndex} >= padLen {padLen} (buffer overrun)");
    }
}

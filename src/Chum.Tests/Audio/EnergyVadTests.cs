using Chum.Audio.Vad;
using Xunit;

namespace Chum.Tests.Audio;

public sealed class EnergyVadTests
{
    // Thresholds (default): on = -40 dBFS ≈ 0.01 linear, off = -45 dBFS ≈ 0.00562 linear

    private static float[] Tone(int count, float amplitude) =>
        Enumerable.Repeat(amplitude, count).ToArray();

    // ── Silence / quiet ───────────────────────────────────────────────────────

    [Fact]
    public void Silence_ReturnsFalse()
    {
        var vad = new EnergyVad();
        Assert.False(vad.IsSpeech(new float[160]));
    }

    [Fact]
    public void EmptySpan_ReturnsFalse()
    {
        var vad = new EnergyVad();
        Assert.False(vad.IsSpeech(ReadOnlySpan<float>.Empty));
    }

    [Fact]
    public void BelowOnThreshold_ReturnsFalse()
    {
        // 0.005 < 0.01 on-threshold → never triggers
        var vad = new EnergyVad();
        Assert.False(vad.IsSpeech(Tone(160, 0.005f)));
    }

    // ── Speech detection ──────────────────────────────────────────────────────

    [Fact]
    public void AboveOnThreshold_ReturnsTrue()
    {
        // 0.1 >> 0.01 on-threshold → triggers
        var vad = new EnergyVad();
        Assert.True(vad.IsSpeech(Tone(160, 0.1f)));
    }

    [Fact]
    public void JustAboveOnThreshold_ReturnsTrue()
    {
        // 0.012 > 0.01 → triggers
        var vad = new EnergyVad();
        Assert.True(vad.IsSpeech(Tone(160, 0.012f)));
    }

    [Fact]
    public void LoudSignal_ReturnsTrue()
    {
        var vad = new EnergyVad();
        Assert.True(vad.IsSpeech(Tone(160, 0.9f)));
    }

    // ── Hysteresis ────────────────────────────────────────────────────────────

    [Fact]
    public void AfterSpeech_ModerateSignal_StaysTrue_UntilBelowOffThreshold()
    {
        var vad = new EnergyVad();
        // 1. Trigger on with loud signal
        vad.IsSpeech(Tone(160, 0.1f));

        // 2. Signal between off-threshold (0.00562) and on-threshold (0.01) → should stay true
        bool stillSpeech = vad.IsSpeech(Tone(160, 0.008f));
        Assert.True(stillSpeech);
    }

    [Fact]
    public void AfterSpeech_SilenceFallsBelowOffThreshold_ReturnsFalse()
    {
        var vad = new EnergyVad();
        // 1. Trigger on
        vad.IsSpeech(Tone(160, 0.1f));

        // 2. Drop below off-threshold (0.001 < 0.00562)
        bool afterSilence = vad.IsSpeech(Tone(160, 0.001f));
        Assert.False(afterSilence);
    }

    [Fact]
    public void Hysteresis_OffThreshold_LowerThanOnThreshold()
    {
        // Verify the hysteresis band works: signal in (off, on) keeps speech active after trigger
        var vad = new EnergyVad(onThresholdDb: -40f, offThresholdDb: -45f);

        // Trigger on
        vad.IsSpeech(Tone(160, 0.1f));

        // Signal at 0.007 is above off-threshold (-45dBFS ≈ 0.00562) but below on-threshold (-40dBFS ≈ 0.01)
        Assert.True(vad.IsSpeech(Tone(160, 0.007f)));
    }

    [Fact]
    public void Hysteresis_ThreePhase_TrueHighTrueModerateFalseLow()
    {
        var vad = new EnergyVad();
        bool phase1 = vad.IsSpeech(Tone(160, 0.1f));   // loud → true
        bool phase2 = vad.IsSpeech(Tone(160, 0.007f)); // hysteresis band → still true
        bool phase3 = vad.IsSpeech(Tone(160, 0.001f)); // below off → false
        Assert.True(phase1);
        Assert.True(phase2);
        Assert.False(phase3);
    }

    // ── State resets correctly ─────────────────────────────────────────────────

    [Fact]
    public void AfterFallingOff_NextLoudSignal_RetriggersTrue()
    {
        var vad = new EnergyVad();
        vad.IsSpeech(Tone(160, 0.1f));  // on
        vad.IsSpeech(Tone(160, 0.001f)); // off
        bool retriggered = vad.IsSpeech(Tone(160, 0.1f)); // on again
        Assert.True(retriggered);
    }

    [Fact]
    public void InitialState_IsNotSpeaking()
    {
        var vad = new EnergyVad();
        // A moderate signal below the on-threshold should stay false without prior trigger
        Assert.False(vad.IsSpeech(Tone(160, 0.007f)));
    }

    // ── Custom thresholds ─────────────────────────────────────────────────────

    [Fact]
    public void CustomThresholds_OnAt20dBFS_HighSensitivity()
    {
        // -20 dBFS on-threshold ≈ 0.1 linear — very sensitive
        var vad = new EnergyVad(onThresholdDb: -20f, offThresholdDb: -25f);
        Assert.True(vad.IsSpeech(Tone(160, 0.2f)));
    }

    [Fact]
    public void CustomThresholds_OnAt10dBFS_LowSensitivity_QuietSignalNotTriggered()
    {
        // -10 dBFS on-threshold ≈ 0.316 linear — requires loud signal
        var vad = new EnergyVad(onThresholdDb: -10f, offThresholdDb: -15f);
        Assert.False(vad.IsSpeech(Tone(160, 0.1f))); // 0.1 < 0.316
    }

    // ── Single sample ─────────────────────────────────────────────────────────

    [Fact]
    public void SingleLoudSample_ReturnsTrue()
    {
        var vad = new EnergyVad();
        Assert.True(vad.IsSpeech(new[] { 0.5f }));
    }

    [Fact]
    public void SingleZeroSample_ReturnsFalse()
    {
        var vad = new EnergyVad();
        Assert.False(vad.IsSpeech(new[] { 0.0f }));
    }
}

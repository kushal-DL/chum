using Chum.Audio.Pipeline;
using Xunit;

namespace Chum.Tests.Audio;

public sealed class NoiseSuppressorTests
{
    private static float Rms(ReadOnlySpan<float> x)
    {
        if (x.Length == 0) return 0f;
        double sum = 0;
        foreach (var s in x) sum += s * (double)s;
        return (float)Math.Sqrt(sum / x.Length);
    }

    [Fact]
    public void Empty_ReturnsEmpty()
    {
        Assert.Empty(NoiseSuppressor.Process(ReadOnlySpan<float>.Empty));
    }

    [Fact]
    public void Silence_ReturnsAllZeros()
    {
        var output = NoiseSuppressor.Process(new float[16000]);
        Assert.All(output, s => Assert.Equal(0f, s, precision: 6));
    }

    [Fact]
    public void Output_PreservesLength()
    {
        var input = new float[8000];
        for (int i = 0; i < input.Length; i++) input[i] = MathF.Sin(i * 0.05f) * 0.2f;
        var output = NoiseSuppressor.Process(input);
        Assert.Equal(input.Length, output.Length);
    }

    [Fact]
    public void ShortInput_DoesNotThrow_PreservesLength()
    {
        // Fewer than 4 frames (160 samples each) → high-pass only, no gate
        var input = new float[100];
        for (int i = 0; i < input.Length; i++) input[i] = 0.3f;
        var output = NoiseSuppressor.Process(input);
        Assert.Equal(100, output.Length);
    }

    [Fact]
    public void DcOffset_RemovedByHighPass()
    {
        // Constant signal is pure DC — high-pass should drive the tail to ~0
        var input = new float[16000];
        Array.Fill(input, 0.5f);
        var output = NoiseSuppressor.Process(input);
        for (int i = output.Length - 1000; i < output.Length; i++)
            Assert.True(Math.Abs(output[i]) < 0.01f, $"sample {i} = {output[i]}");
    }

    [Fact]
    public void DcOffset_OutputEnergyMuchLowerThanInput()
    {
        var input = new float[16000];
        Array.Fill(input, 0.5f);
        var output = NoiseSuppressor.Process(input);
        Assert.True(Rms(output) < Rms(input) * 0.2f);
    }

    [Fact]
    public void LoudSpeechAfterQuiet_RetainedLouderThanGatedNoise()
    {
        // First half: quiet room noise. Second half: loud "speech".
        int half = 8000;
        var input = new float[half * 2];
        for (int i = 0; i < half; i++) input[i] = MathF.Sin(i * 0.1f) * 0.002f;            // quiet
        for (int i = 0; i < half; i++) input[half + i] = MathF.Sin(i * 0.12f) * 0.3f;      // loud

        var output = NoiseSuppressor.Process(input);

        float quietRms = Rms(output.AsSpan(0, half));
        float loudRms = Rms(output.AsSpan(half, half));

        Assert.True(loudRms > 0.05f, $"loud half too quiet: {loudRms}");
        Assert.True(loudRms > quietRms * 3f, $"gate did not separate loud/quiet: loud={loudRms} quiet={quietRms}");
    }

    [Fact]
    public void SteadyLowNoise_IsAttenuated()
    {
        // Uniform low-level noise with no loud content → gate closes, energy drops
        var rng = new Random(42);
        var input = new float[16000];
        for (int i = 0; i < input.Length; i++) input[i] = (float)(rng.NextDouble() - 0.5) * 0.01f;
        var output = NoiseSuppressor.Process(input);
        Assert.True(Rms(output) <= Rms(input));
    }

    [Fact]
    public void LoudSignal_NotFullyDestroyed()
    {
        // A clearly-above-floor signal preceded by a short quiet lead-in must survive the gate
        var input = new float[16000];
        for (int i = 0; i < 1600; i++) input[i] = MathF.Sin(i * 0.1f) * 0.001f;   // 0.1s quiet lead-in
        for (int i = 1600; i < input.Length; i++) input[i] = MathF.Sin(i * 0.1f) * 0.4f;
        var output = NoiseSuppressor.Process(input);
        Assert.True(Rms(output.AsSpan(2000)) > 0.05f);
    }
}

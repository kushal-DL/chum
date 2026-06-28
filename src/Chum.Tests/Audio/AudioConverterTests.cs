using Chum.Audio.Pipeline;
using NAudio.Wave;
using Xunit;

namespace Chum.Tests.Audio;

public sealed class AudioConverterTests
{
    // ── Helper: float[] → IEEE float byte buffer ──────────────────────────────

    private static byte[] ToIeeeFloatBytes(float[] samples)
    {
        var bytes = new byte[samples.Length * 4];
        Buffer.BlockCopy(samples, 0, bytes, 0, bytes.Length);
        return bytes;
    }

    private static byte[] ToPcm16Bytes(short[] samples)
    {
        var bytes = new byte[samples.Length * 2];
        Buffer.BlockCopy(samples, 0, bytes, 0, bytes.Length);
        return bytes;
    }

    // ── Mono IEEE float at target rate — passthrough ──────────────────────────

    [Fact]
    public void MonoFloat_AtTargetRate_ReturnsSameSamples()
    {
        float[] input = { 0.5f, -0.25f, 0.1f };
        var fmt = WaveFormat.CreateIeeeFloatWaveFormat(16000, 1);
        byte[] buffer = ToIeeeFloatBytes(input);

        float[] output = AudioConverter.ToMono16kHz(buffer, buffer.Length, fmt);

        Assert.Equal(input.Length, output.Length);
        for (int i = 0; i < input.Length; i++)
            Assert.Equal(input[i], output[i], precision: 5);
    }

    [Fact]
    public void MonoFloat_AllZeros_ReturnZeros()
    {
        float[] input = new float[64];
        var fmt = WaveFormat.CreateIeeeFloatWaveFormat(16000, 1);
        byte[] buffer = ToIeeeFloatBytes(input);

        float[] output = AudioConverter.ToMono16kHz(buffer, buffer.Length, fmt);

        Assert.All(output, s => Assert.Equal(0f, s));
    }

    // ── Stereo → mono conversion ──────────────────────────────────────────────

    [Fact]
    public void StereoFloat_AveragesLeftAndRight()
    {
        // Two frames: [1.0, 0.0] and [0.5, -0.5]
        float[] input = { 1.0f, 0.0f, 0.5f, -0.5f };
        var fmt = WaveFormat.CreateIeeeFloatWaveFormat(16000, 2);
        byte[] buffer = ToIeeeFloatBytes(input);

        float[] output = AudioConverter.ToMono16kHz(buffer, buffer.Length, fmt);

        Assert.Equal(2, output.Length);
        Assert.Equal(0.5f, output[0], precision: 5); // (1.0 + 0.0) / 2
        Assert.Equal(0.0f, output[1], precision: 5); // (0.5 + -0.5) / 2
    }

    [Fact]
    public void StereoFloat_BothChannelsEqual_MonoEqualsSingleChannel()
    {
        float[] input = { 0.3f, 0.3f, -0.6f, -0.6f };
        var fmt = WaveFormat.CreateIeeeFloatWaveFormat(16000, 2);
        byte[] buffer = ToIeeeFloatBytes(input);

        float[] output = AudioConverter.ToMono16kHz(buffer, buffer.Length, fmt);

        Assert.Equal(2, output.Length);
        Assert.Equal(0.3f, output[0], precision: 5);
        Assert.Equal(-0.6f, output[1], precision: 5);
    }

    // ── PCM 16-bit decoding ───────────────────────────────────────────────────

    [Fact]
    public void Pcm16_Zero_DecodesTo_Zero()
    {
        var fmt = new WaveFormat(16000, 16, 1);
        byte[] buffer = ToPcm16Bytes(new short[] { 0 });

        float[] output = AudioConverter.ToMono16kHz(buffer, buffer.Length, fmt);

        Assert.Single(output);
        Assert.Equal(0f, output[0], precision: 4);
    }

    [Fact]
    public void Pcm16_MaxPositive_DecodesTo_NearOne()
    {
        var fmt = new WaveFormat(16000, 16, 1);
        byte[] buffer = ToPcm16Bytes(new short[] { short.MaxValue }); // 32767

        float[] output = AudioConverter.ToMono16kHz(buffer, buffer.Length, fmt);

        Assert.InRange(output[0], 0.99f, 1.001f);
    }

    [Fact]
    public void Pcm16_MaxNegative_DecodesTo_NearMinusOne()
    {
        var fmt = new WaveFormat(16000, 16, 1);
        byte[] buffer = ToPcm16Bytes(new short[] { short.MinValue }); // -32768

        float[] output = AudioConverter.ToMono16kHz(buffer, buffer.Length, fmt);

        Assert.InRange(output[0], -1.001f, -0.99f);
    }

    // ── Resampling ────────────────────────────────────────────────────────────

    [Fact]
    public void Downsample_48kTo16k_OutputLengthIsCorrect()
    {
        int inputSamples = 4800; // 0.1s at 48kHz
        float[] input = new float[inputSamples];
        var fmt = WaveFormat.CreateIeeeFloatWaveFormat(48000, 1);
        byte[] buffer = ToIeeeFloatBytes(input);

        float[] output = AudioConverter.ToMono16kHz(buffer, buffer.Length, fmt);

        // 4800 * 16000 / 48000 = 1600
        Assert.Equal(1600, output.Length);
    }

    [Fact]
    public void Downsample_44100To16k_OutputLengthIsApproximate()
    {
        int inputSamples = 4410;
        float[] input = new float[inputSamples];
        var fmt = WaveFormat.CreateIeeeFloatWaveFormat(44100, 1);
        byte[] buffer = ToIeeeFloatBytes(input);

        float[] output = AudioConverter.ToMono16kHz(buffer, buffer.Length, fmt);

        // 4410 * 16000 / 44100 ≈ 1600
        int expected = (int)((long)inputSamples * 16000 / 44100);
        Assert.Equal(expected, output.Length);
    }

    [Fact]
    public void Downsample_ConstantSignal_OutputIsConstant()
    {
        // A constant DC signal should downsample to the same constant
        float dc = 0.4f;
        int n = 4800;
        float[] input = Enumerable.Repeat(dc, n).ToArray();
        var fmt = WaveFormat.CreateIeeeFloatWaveFormat(48000, 1);
        byte[] buffer = ToIeeeFloatBytes(input);

        float[] output = AudioConverter.ToMono16kHz(buffer, buffer.Length, fmt);

        // All output samples should equal the DC value (linear interpolation of constant = constant)
        Assert.All(output, s => Assert.Equal(dc, s, precision: 4));
    }

    [Fact]
    public void SameRate_DoesNotResample()
    {
        float[] input = { 0.1f, 0.2f, 0.3f };
        var fmt = WaveFormat.CreateIeeeFloatWaveFormat(16000, 1);
        byte[] buffer = ToIeeeFloatBytes(input);

        float[] output = AudioConverter.ToMono16kHz(buffer, buffer.Length, fmt);

        Assert.Equal(input.Length, output.Length);
    }
}

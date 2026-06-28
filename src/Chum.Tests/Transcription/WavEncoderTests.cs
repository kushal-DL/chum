using System.IO;
using Chum.Transcription;
using Xunit;

namespace Chum.Tests.Transcription;

public sealed class WavEncoderTests
{
    // ── Header structure ──────────────────────────────────────────────────────

    [Fact]
    public void Silence_HasCorrectRiffHeader()
    {
        var bytes = WavEncoder.ToWavBytes(new float[16000]);
        Assert.Equal('R', (char)bytes[0]);
        Assert.Equal('I', (char)bytes[1]);
        Assert.Equal('F', (char)bytes[2]);
        Assert.Equal('F', (char)bytes[3]);
    }

    [Fact]
    public void Silence_HasWaveMarker()
    {
        var bytes = WavEncoder.ToWavBytes(new float[16000]);
        Assert.Equal('W', (char)bytes[8]);
        Assert.Equal('A', (char)bytes[9]);
        Assert.Equal('V', (char)bytes[10]);
        Assert.Equal('E', (char)bytes[11]);
    }

    [Fact]
    public void Silence_HasFmtChunk()
    {
        var bytes = WavEncoder.ToWavBytes(new float[16000]);
        Assert.Equal('f', (char)bytes[12]);
        Assert.Equal('m', (char)bytes[13]);
        Assert.Equal('t', (char)bytes[14]);
        Assert.Equal(' ', (char)bytes[15]);
    }

    [Fact]
    public void Silence_HasDataChunk()
    {
        var bytes = WavEncoder.ToWavBytes(new float[16000]);
        Assert.Equal('d', (char)bytes[36]);
        Assert.Equal('a', (char)bytes[37]);
        Assert.Equal('t', (char)bytes[38]);
        Assert.Equal('a', (char)bytes[39]);
    }

    [Fact]
    public void Header_TotalLengthIs44Bytes()
    {
        // WAV header is always 44 bytes (RIFF + WAVE + fmt(24) + data(8))
        var bytes = WavEncoder.ToWavBytes(Array.Empty<float>());
        Assert.Equal(44, bytes.Length);
    }

    // ── Size calculations ─────────────────────────────────────────────────────

    [Fact]
    public void TotalSize_EqualsSamplesTimesTwo_PlusHeader()
    {
        int samples = 16000; // 1 second at 16 kHz
        var bytes = WavEncoder.ToWavBytes(new float[samples]);
        Assert.Equal(44 + samples * 2, bytes.Length); // 16-bit = 2 bytes/sample
    }

    [Fact]
    public void ChunkSize_Field_EqualsDataSize_Plus36()
    {
        int samples = 800;
        var bytes = WavEncoder.ToWavBytes(new float[samples]);
        int chunkSize = BitConverter.ToInt32(bytes, 4);
        Assert.Equal(36 + samples * 2, chunkSize);
    }

    [Fact]
    public void DataChunkSize_FieldMatchesSampleBytes()
    {
        int samples = 3200;
        var bytes = WavEncoder.ToWavBytes(new float[samples]);
        int dataSize = BitConverter.ToInt32(bytes, 40);
        Assert.Equal(samples * 2, dataSize);
    }

    // ── PCM format fields ─────────────────────────────────────────────────────

    [Fact]
    public void AudioFormat_IsPCM_1()
    {
        var bytes = WavEncoder.ToWavBytes(new float[16]);
        short audioFormat = BitConverter.ToInt16(bytes, 20);
        Assert.Equal(1, audioFormat);
    }

    [Fact]
    public void Channels_IsMono_1()
    {
        var bytes = WavEncoder.ToWavBytes(new float[16]);
        short channels = BitConverter.ToInt16(bytes, 22);
        Assert.Equal(1, channels);
    }

    [Fact]
    public void SampleRate_Is16000()
    {
        var bytes = WavEncoder.ToWavBytes(new float[16]);
        int sampleRate = BitConverter.ToInt32(bytes, 24);
        Assert.Equal(16000, sampleRate);
    }

    [Fact]
    public void BitsPerSample_Is16()
    {
        var bytes = WavEncoder.ToWavBytes(new float[16]);
        short bitsPerSample = BitConverter.ToInt16(bytes, 34);
        Assert.Equal(16, bitsPerSample);
    }

    [Fact]
    public void CustomSampleRate_IsStoredCorrectly()
    {
        var bytes = WavEncoder.ToWavBytes(new float[16], sampleRate: 44100);
        int sampleRate = BitConverter.ToInt32(bytes, 24);
        Assert.Equal(44100, sampleRate);
    }

    // ── Sample encoding ───────────────────────────────────────────────────────

    [Fact]
    public void Silence_AllSampleBytesAreZero()
    {
        var bytes = WavEncoder.ToWavBytes(new float[4]);
        for (int i = 44; i < bytes.Length; i++)
            Assert.Equal(0, bytes[i]);
    }

    [Fact]
    public void MaxPositiveSample_EncodesTo32767()
    {
        var bytes = WavEncoder.ToWavBytes(new[] { 1.0f });
        short s = BitConverter.ToInt16(bytes, 44);
        Assert.Equal(32767, s);
    }

    [Fact]
    public void MaxNegativeSample_EncodesToMinus32767()
    {
        // Formula: s * 32767f → -1.0 * 32767 = -32767 (not -32768; clamp is only for overdrive)
        var bytes = WavEncoder.ToWavBytes(new[] { -1.0f });
        short s = BitConverter.ToInt16(bytes, 44);
        Assert.Equal(-32767, s);
    }

    [Fact]
    public void HalfPositive_EncodesTo16383()
    {
        var bytes = WavEncoder.ToWavBytes(new[] { 0.5f });
        short s = BitConverter.ToInt16(bytes, 44);
        Assert.InRange(s, (short)16383, (short)16384);
    }

    [Fact]
    public void Clamp_OverdriveSample_DoesNotOverflow()
    {
        var bytes = WavEncoder.ToWavBytes(new[] { 2.0f, -2.0f });
        short pos = BitConverter.ToInt16(bytes, 44);
        short neg = BitConverter.ToInt16(bytes, 46);
        Assert.Equal(short.MaxValue, pos);
        Assert.Equal(short.MinValue, neg);
    }

    [Fact]
    public void MultipleSamples_OrderPreserved()
    {
        var bytes = WavEncoder.ToWavBytes(new[] { 0.0f, 1.0f, -1.0f });
        short s0 = BitConverter.ToInt16(bytes, 44);
        short s1 = BitConverter.ToInt16(bytes, 46);
        short s2 = BitConverter.ToInt16(bytes, 48);
        Assert.Equal(0, s0);
        Assert.Equal(32767, s1);
        Assert.Equal(-32767, s2); // -1.0 * 32767 = -32767
    }

    // ── Readable as valid WAV (smoke test) ────────────────────────────────────

    [Fact]
    public void Output_IsReadableByBinaryReader()
    {
        float[] tone = Enumerable.Range(0, 1600)
            .Select(i => MathF.Sin(2 * MathF.PI * 440 * i / 16000f) * 0.5f)
            .ToArray();
        var bytes = WavEncoder.ToWavBytes(tone);
        using var ms = new MemoryStream(bytes);
        using var reader = new BinaryReader(ms);
        string riff = new(reader.ReadChars(4));
        Assert.Equal("RIFF", riff);
    }
}

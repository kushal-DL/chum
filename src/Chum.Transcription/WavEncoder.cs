using System.IO;

namespace Chum.Transcription;

/// <summary>
/// Converts raw float PCM samples to WAV bytes suitable for sending to LLM audio APIs.
/// </summary>
public static class WavEncoder
{
    /// <summary>
    /// Encodes float PCM samples (range -1.0 to 1.0) into a WAV file byte array.
    /// Output: 16-bit PCM, mono, <paramref name="sampleRate"/> Hz.
    /// </summary>
    public static byte[] ToWavBytes(float[] samples, int sampleRate = 16000)
    {
        const int numChannels = 1;
        const int bitsPerSample = 16;
        int byteRate = sampleRate * numChannels * bitsPerSample / 8;
        int blockAlign = numChannels * bitsPerSample / 8;
        int dataSize = samples.Length * blockAlign;

        using var ms = new MemoryStream(44 + dataSize);
        using var w = new BinaryWriter(ms);

        // RIFF header
        w.Write("RIFF".ToCharArray());
        w.Write(36 + dataSize);
        w.Write("WAVE".ToCharArray());

        // fmt chunk
        w.Write("fmt ".ToCharArray());
        w.Write(16);                    // Subchunk1Size for PCM
        w.Write((short)1);              // AudioFormat: PCM
        w.Write((short)numChannels);
        w.Write(sampleRate);
        w.Write(byteRate);
        w.Write((short)blockAlign);
        w.Write((short)bitsPerSample);

        // data chunk
        w.Write("data".ToCharArray());
        w.Write(dataSize);

        foreach (var s in samples)
            w.Write((short)Math.Clamp((int)(s * 32767f), short.MinValue, short.MaxValue));

        return ms.ToArray();
    }
}

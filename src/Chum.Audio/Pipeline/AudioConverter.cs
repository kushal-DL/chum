using NAudio.Wave;

namespace Chum.Audio.Pipeline;

/// <summary>Converts raw WASAPI bytes to 16 kHz mono float[] via linear interpolation resampling.</summary>
internal static class AudioConverter
{
    private const int TargetSampleRate = 16_000;

    public static float[] ToMono16kHz(byte[] buffer, int bytesRecorded, WaveFormat fmt)
    {
        float[] rawSamples = ToFloat(buffer, bytesRecorded, fmt);
        float[] mono = ToMono(rawSamples, fmt.Channels);
        return fmt.SampleRate == TargetSampleRate ? mono : Resample(mono, fmt.SampleRate, TargetSampleRate);
    }

    private static float[] ToFloat(byte[] buffer, int bytesRecorded, WaveFormat fmt)
    {
        int bytesPerSample = fmt.BitsPerSample / 8;
        int sampleCount = bytesRecorded / bytesPerSample;
        var samples = new float[sampleCount];

        if (fmt.Encoding == WaveFormatEncoding.IeeeFloat)
        {
            Buffer.BlockCopy(buffer, 0, samples, 0, bytesRecorded);
        }
        else if (fmt.Encoding == WaveFormatEncoding.Pcm && fmt.BitsPerSample == 16)
        {
            for (int i = 0; i < sampleCount; i++)
                samples[i] = BitConverter.ToInt16(buffer, i * 2) / 32768f;
        }
        else if (fmt.Encoding == WaveFormatEncoding.Pcm && fmt.BitsPerSample == 32)
        {
            for (int i = 0; i < sampleCount; i++)
                samples[i] = BitConverter.ToInt32(buffer, i * 4) / 2147483648f;
        }

        return samples;
    }

    private static float[] ToMono(float[] samples, int channels)
    {
        if (channels == 1) return samples;
        int monoCount = samples.Length / channels;
        var mono = new float[monoCount];
        for (int i = 0; i < monoCount; i++)
        {
            float sum = 0f;
            for (int c = 0; c < channels; c++)
                sum += samples[i * channels + c];
            mono[i] = sum / channels;
        }
        return mono;
    }

    private static float[] Resample(float[] input, int fromRate, int toRate)
    {
        int outputLen = (int)((long)input.Length * toRate / fromRate);
        var output = new float[outputLen];
        double ratio = (double)(input.Length - 1) / Math.Max(outputLen - 1, 1);
        for (int i = 0; i < outputLen; i++)
        {
            double src = i * ratio;
            int lo = (int)src;
            double frac = src - lo;
            float a = input[lo];
            float b = lo + 1 < input.Length ? input[lo + 1] : a;
            output[i] = (float)(a + frac * (b - a));
        }
        return output;
    }
}

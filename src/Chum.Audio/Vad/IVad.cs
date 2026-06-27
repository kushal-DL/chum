namespace Chum.Audio.Vad;

public interface IVad
{
    bool IsSpeech(ReadOnlySpan<float> samples);
}

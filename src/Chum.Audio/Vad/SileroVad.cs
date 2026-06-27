using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace Chum.Audio.Vad;

/// <summary>
/// Silero VAD v5 — ONNX-based voice activity detector.
/// Requires a separate instance per audio source: each stream carries its own LSTM hidden state.
/// </summary>
public sealed class SileroVad : IVad, IDisposable
{
    // Silero v5 expects 512-sample chunks at 16 kHz (32 ms per inference call)
    private const int ChunkSize = 512;
    private const long InferenceSampleRate = 16_000;
    private const int HiddenDim = 64;
    // h and c tensors are shape [2, 1, HiddenDim] — 2 LSTM layers, batch=1
    private const int HiddenTotal = 2 * HiddenDim;

    private readonly InferenceSession _session;
    private readonly float _startThreshold;
    private readonly float _endThreshold;
    private float[] _h = new float[HiddenTotal];
    private float[] _c = new float[HiddenTotal];
    private bool _isSpeaking;

    public SileroVad(string modelPath, float startThreshold = 0.5f, float endThreshold = 0.35f)
    {
        _session = new InferenceSession(modelPath);
        _startThreshold = startThreshold;
        _endThreshold = endThreshold;
    }

    public bool IsSpeech(ReadOnlySpan<float> samples)
    {
        if (samples.IsEmpty) return _isSpeaking;

        int offset = 0;
        while (offset < samples.Length)
        {
            float prob;
            int remaining = samples.Length - offset;
            if (remaining >= ChunkSize)
            {
                prob = RunChunk(samples.Slice(offset, ChunkSize));
                offset += ChunkSize;
            }
            else
            {
                // Zero-pad the final partial chunk so the tensor is always [1, 512]
                float[] padded = new float[ChunkSize];
                samples.Slice(offset).CopyTo(padded);
                prob = RunChunk(padded);
                offset = samples.Length;
            }

            if (!_isSpeaking && prob >= _startThreshold)
                _isSpeaking = true;
            else if (_isSpeaking && prob < _endThreshold)
                _isSpeaking = false;
        }

        return _isSpeaking;
    }

    /// <summary>
    /// Resets LSTM state — call when resuming after a privacy pause so stale context does not bleed.
    /// </summary>
    public void ResetState()
    {
        Array.Clear(_h);
        Array.Clear(_c);
        _isSpeaking = false;
    }

    private float RunChunk(ReadOnlySpan<float> chunk)
    {
        var inputTensor = new DenseTensor<float>(new[] { 1, ChunkSize });
        for (int i = 0; i < ChunkSize; i++)
            inputTensor[0, i] = chunk[i];

        var srTensor = new DenseTensor<long>(new[] { 1 });
        srTensor[0] = InferenceSampleRate;

        var hTensor = new DenseTensor<float>(new[] { 2, 1, HiddenDim });
        _h.AsSpan().CopyTo(hTensor.Buffer.Span);

        var cTensor = new DenseTensor<float>(new[] { 2, 1, HiddenDim });
        _c.AsSpan().CopyTo(cTensor.Buffer.Span);

        var inputs = new[]
        {
            NamedOnnxValue.CreateFromTensor("input", inputTensor),
            NamedOnnxValue.CreateFromTensor("sr",    srTensor),
            NamedOnnxValue.CreateFromTensor("h",     hTensor),
            NamedOnnxValue.CreateFromTensor("c",     cTensor),
        };

        using var outputs = _session.Run(inputs);

        // outputs[0] = speech probability [1,1]; outputs[1] = hn; outputs[2] = cn
        float prob = outputs[0].AsTensor<float>()[0, 0];

        int idx = 0;
        foreach (var v in outputs[1].AsEnumerable<float>()) _h[idx++] = v;
        idx = 0;
        foreach (var v in outputs[2].AsEnumerable<float>()) _c[idx++] = v;

        return prob;
    }

    public void Dispose() => _session.Dispose();
}

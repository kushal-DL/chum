using System.Diagnostics;

namespace Chum.App.Services;

/// <summary>
/// Rolling buffers for STT and LLM first-token latency.
/// Computes p50/p90/p99 over the last 1000 samples.
/// Fires SlowTranscriptionDetected when 3+ consecutive STT segments exceed 15s.
/// </summary>
public sealed class PipelineLatencyTracker
{
    private const int BufferCapacity = 1000;
    private const double SlowThresholdSeconds = 15.0;
    private const int SlowAlertThreshold = 3;

    // STT buffer
    private readonly double[] _sttSeconds = new double[BufferCapacity];
    private int _sttHead;
    private int _sttCount;
    private int _consecutiveSlowCount;

    // LLM first-token buffer
    private readonly double[] _llmMs = new double[BufferCapacity];
    private int _llmHead;
    private int _llmCount;

    private readonly Lock _lock = new();

    /// <summary>Fires when STT latency exceeds 15s for 3 or more consecutive segments.</summary>
    public event EventHandler? SlowTranscriptionDetected;

    public void Record(TimeSpan sttDuration)
    {
        lock (_lock)
        {
            _sttSeconds[_sttHead] = sttDuration.TotalSeconds;
            _sttHead = (_sttHead + 1) % BufferCapacity;
            if (_sttCount < BufferCapacity) _sttCount++;

            if (sttDuration.TotalSeconds > SlowThresholdSeconds)
            {
                if (++_consecutiveSlowCount >= SlowAlertThreshold)
                    SlowTranscriptionDetected?.Invoke(this, EventArgs.Empty);
            }
            else
            {
                _consecutiveSlowCount = 0;
            }
        }
    }

    public void RecordLlmLatency(TimeSpan firstTokenDelay)
    {
        lock (_lock)
        {
            _llmMs[_llmHead] = firstTokenDelay.TotalMilliseconds;
            _llmHead = (_llmHead + 1) % BufferCapacity;
            if (_llmCount < BufferCapacity) _llmCount++;
        }
    }

    public (double P50, double P90, double P99) GetPercentiles()
    {
        lock (_lock)
        {
            if (_sttCount == 0) return (0, 0, 0);
            var sorted = new double[_sttCount];
            int start = _sttCount < BufferCapacity ? 0 : _sttHead;
            for (int i = 0; i < _sttCount; i++)
                sorted[i] = _sttSeconds[(start + i) % BufferCapacity];
            Array.Sort(sorted);
            return (Percentile(sorted, 0.50), Percentile(sorted, 0.90), Percentile(sorted, 0.99));
        }
    }

    public (double P50, double P90, double P99) GetLlmPercentiles()
    {
        lock (_lock)
        {
            if (_llmCount == 0) return (0, 0, 0);
            var sorted = new double[_llmCount];
            int start = _llmCount < BufferCapacity ? 0 : _llmHead;
            for (int i = 0; i < _llmCount; i++)
                sorted[i] = _llmMs[(start + i) % BufferCapacity];
            Array.Sort(sorted);
            return (Percentile(sorted, 0.50), Percentile(sorted, 0.90), Percentile(sorted, 0.99));
        }
    }

    public int SegmentsRecorded { get { lock (_lock) return _sttCount; } }
    public int LlmQueriesRecorded { get { lock (_lock) return _llmCount; } }

    private static double Percentile(double[] sorted, double p)
    {
        double idx = p * (sorted.Length - 1);
        int lo = (int)idx;
        int hi = Math.Min(lo + 1, sorted.Length - 1);
        return sorted[lo] + (idx - lo) * (sorted[hi] - sorted[lo]);
    }
}

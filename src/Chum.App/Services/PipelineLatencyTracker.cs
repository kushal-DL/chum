using System.Diagnostics;

namespace Chum.App.Services;

/// <summary>
/// Rolling buffer of per-segment STT latency measurements.
/// Computes p50/p90/p99 over the last 1000 segments.
/// Fires SlowTranscriptionDetected when 3+ consecutive segments exceed 15s.
/// </summary>
public sealed class PipelineLatencyTracker
{
    private const int BufferCapacity = 1000;
    private const double SlowThresholdSeconds = 15.0;
    private const int SlowAlertThreshold = 3;

    private readonly double[] _sttSeconds = new double[BufferCapacity];
    private int _head;
    private int _count;
    private int _consecutiveSlowCount;
    private readonly Lock _lock = new();

    /// <summary>Fires when STT latency exceeds 15s for 3 or more consecutive segments.</summary>
    public event EventHandler? SlowTranscriptionDetected;

    public void Record(TimeSpan sttDuration)
    {
        lock (_lock)
        {
            _sttSeconds[_head] = sttDuration.TotalSeconds;
            _head = (_head + 1) % BufferCapacity;
            if (_count < BufferCapacity) _count++;

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

    public (double P50, double P90, double P99) GetPercentiles()
    {
        lock (_lock)
        {
            if (_count == 0) return (0, 0, 0);
            var sorted = new double[_count];
            int start = _count < BufferCapacity ? 0 : _head;
            for (int i = 0; i < _count; i++)
                sorted[i] = _sttSeconds[(start + i) % BufferCapacity];
            Array.Sort(sorted);
            return (Percentile(sorted, 0.50), Percentile(sorted, 0.90), Percentile(sorted, 0.99));
        }
    }

    public int SegmentsRecorded { get { lock (_lock) return _count; } }

    private static double Percentile(double[] sorted, double p)
    {
        double idx = p * (sorted.Length - 1);
        int lo = (int)idx;
        int hi = Math.Min(lo + 1, sorted.Length - 1);
        return sorted[lo] + (idx - lo) * (sorted[hi] - sorted[lo]);
    }
}

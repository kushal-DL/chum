using Chum.Llm;

namespace Chum.App.Services;

public sealed class SessionCostTracker
{
    private readonly Lock _lock = new();
    private int _queryCount;
    private int _totalInputTokens;
    private int _totalOutputTokens;
    private decimal _totalCostUsd;
    private LlmUsage? _lastUsage;

    public event EventHandler? ThresholdExceeded;

    public void Record(LlmUsage usage, decimal thresholdUsd)
    {
        lock (_lock)
        {
            _lastUsage = usage;
            _totalInputTokens += usage.InputTokens;
            _totalOutputTokens += usage.OutputTokens;
            decimal before = _totalCostUsd;
            _totalCostUsd += usage.EstimatedCostUsd;
            _queryCount++;
            if (thresholdUsd > 0 && before < thresholdUsd && _totalCostUsd >= thresholdUsd)
                ThresholdExceeded?.Invoke(this, EventArgs.Empty);
        }
    }

    public void Reset()
    {
        lock (_lock)
        {
            _queryCount = 0;
            _totalInputTokens = 0;
            _totalOutputTokens = 0;
            _totalCostUsd = 0m;
            _lastUsage = null;
        }
    }

    public (int Queries, int InputTokens, int OutputTokens, decimal TotalCostUsd, LlmUsage? LastUsage)
        GetStats()
    {
        lock (_lock)
            return (_queryCount, _totalInputTokens, _totalOutputTokens, _totalCostUsd, _lastUsage);
    }
}

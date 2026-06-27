namespace Chum.Llm;

public sealed record LlmUsage(
    string Model,
    int InputTokens,
    int OutputTokens,
    decimal EstimatedCostUsd
);

public static class LlmPricing
{
    private static readonly Dictionary<string, (decimal In, decimal Out)> _prices =
        new(StringComparer.OrdinalIgnoreCase)
    {
        ["claude-haiku-4-5-20251001"] = (0.80m, 4.00m),
        ["claude-haiku-4-5"]          = (0.80m, 4.00m),
        ["claude-sonnet-4-6"]         = (3.00m, 15.00m),
        ["claude-opus-4-8"]           = (15.00m, 75.00m),
        ["gpt-4o-mini"]               = (0.15m, 0.60m),
        ["gpt-4o-mini-2024-07-18"]    = (0.15m, 0.60m),
        ["gpt-4o"]                    = (2.50m, 10.00m),
        ["gpt-4o-2024-11-20"]         = (2.50m, 10.00m),
        ["gpt-4-turbo"]               = (10.00m, 30.00m),
    };

    public static decimal EstimateCost(string model, int inputTokens, int outputTokens)
    {
        if (!_prices.TryGetValue(model, out var p)) return 0m;
        return (inputTokens * p.In / 1_000_000m) + (outputTokens * p.Out / 1_000_000m);
    }
}

using Chum.Llm;
using Xunit;

namespace Chum.Tests.Llm;

public sealed class LlmPricingTests
{
    // ── Known models — correct cost ───────────────────────────────────────────

    [Fact]
    public void ClaudeHaiku_ZeroTokens_ReturnsZero()
    {
        var cost = LlmPricing.EstimateCost("claude-haiku-4-5-20251001", 0, 0);
        Assert.Equal(0m, cost);
    }

    [Fact]
    public void ClaudeHaiku_1M_InputTokens_Costs_80Cents()
    {
        // $0.80 per 1M input tokens
        var cost = LlmPricing.EstimateCost("claude-haiku-4-5-20251001", 1_000_000, 0);
        Assert.Equal(0.80m, cost);
    }

    [Fact]
    public void ClaudeHaiku_1M_OutputTokens_Costs_4Dollars()
    {
        // $4.00 per 1M output tokens
        var cost = LlmPricing.EstimateCost("claude-haiku-4-5-20251001", 0, 1_000_000);
        Assert.Equal(4.00m, cost);
    }

    [Fact]
    public void ClaudeHaiku_BothInputAndOutput_CombineCorrectly()
    {
        var cost = LlmPricing.EstimateCost("claude-haiku-4-5-20251001", 1_000_000, 1_000_000);
        Assert.Equal(4.80m, cost);
    }

    [Fact]
    public void ClaudeHaiku_ShortAlias_WorksLikeLongAlias()
    {
        var full = LlmPricing.EstimateCost("claude-haiku-4-5-20251001", 500, 200);
        var alias = LlmPricing.EstimateCost("claude-haiku-4-5", 500, 200);
        Assert.Equal(full, alias);
    }

    [Fact]
    public void ClaudeSonnet_1M_InputTokens_Costs_3Dollars()
    {
        var cost = LlmPricing.EstimateCost("claude-sonnet-4-6", 1_000_000, 0);
        Assert.Equal(3.00m, cost);
    }

    [Fact]
    public void ClaudeOpus_1M_InputTokens_Costs_15Dollars()
    {
        var cost = LlmPricing.EstimateCost("claude-opus-4-8", 1_000_000, 0);
        Assert.Equal(15.00m, cost);
    }

    [Fact]
    public void Gpt4oMini_1M_InputTokens_Costs_15Cents()
    {
        var cost = LlmPricing.EstimateCost("gpt-4o-mini", 1_000_000, 0);
        Assert.Equal(0.15m, cost);
    }

    [Fact]
    public void Gpt4o_1M_InputTokens_Costs_2Dollars50()
    {
        var cost = LlmPricing.EstimateCost("gpt-4o", 1_000_000, 0);
        Assert.Equal(2.50m, cost);
    }

    [Fact]
    public void Gpt4Turbo_1M_InputTokens_Costs_10Dollars()
    {
        var cost = LlmPricing.EstimateCost("gpt-4-turbo", 1_000_000, 0);
        Assert.Equal(10.00m, cost);
    }

    // ── Case insensitivity ────────────────────────────────────────────────────

    [Fact]
    public void ModelNameLookup_IsCaseInsensitive()
    {
        var lower = LlmPricing.EstimateCost("claude-sonnet-4-6", 1000, 500);
        var upper = LlmPricing.EstimateCost("CLAUDE-SONNET-4-6", 1000, 500);
        var mixed = LlmPricing.EstimateCost("Claude-Sonnet-4-6", 1000, 500);
        Assert.Equal(lower, upper);
        Assert.Equal(lower, mixed);
    }

    // ── Unknown models ────────────────────────────────────────────────────────

    [Fact]
    public void UnknownModel_ReturnsZero()
    {
        var cost = LlmPricing.EstimateCost("some-unknown-model-xyz", 100_000, 50_000);
        Assert.Equal(0m, cost);
    }

    [Fact]
    public void EmptyModelName_ReturnsZero()
    {
        var cost = LlmPricing.EstimateCost(string.Empty, 1000, 1000);
        Assert.Equal(0m, cost);
    }

    // ── Small token counts ────────────────────────────────────────────────────

    [Fact]
    public void SmallTokenCount_ReturnsPositiveNonZeroCost()
    {
        // 1000 input tokens of Sonnet = 3.00 / 1000 = 0.000003 USD
        var cost = LlmPricing.EstimateCost("claude-sonnet-4-6", 1000, 0);
        Assert.True(cost > 0m);
    }

    [Fact]
    public void TypicalUsage_CostIsReasonable()
    {
        // 500 input + 150 output at Haiku prices should be fractions of a cent
        var cost = LlmPricing.EstimateCost("claude-haiku-4-5", 500, 150);
        Assert.True(cost > 0m);
        Assert.True(cost < 0.01m); // well under a cent
    }
}

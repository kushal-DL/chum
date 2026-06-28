using Chum.Llm;
using Xunit;

namespace Chum.Tests.Llm;

public sealed class PromptTemplateTests
{
    // ── BuiltIns collection ───────────────────────────────────────────────────

    [Fact]
    public void BuiltIns_HasFiveTemplates()
    {
        Assert.Equal(5, PromptTemplate.BuiltIns.Count);
    }

    [Fact]
    public void BuiltIns_FirstEntryIsDefault()
    {
        Assert.Same(PromptTemplate.Default, PromptTemplate.BuiltIns[0]);
    }

    [Fact]
    public void BuiltIns_ContainsQuickAnswer()
    {
        Assert.Contains(PromptTemplate.BuiltIns, t => t.Name == "Quick Answer");
    }

    [Fact]
    public void BuiltIns_ContainsDetailedExplanation()
    {
        Assert.Contains(PromptTemplate.BuiltIns, t => t.Name == "Detailed Explanation");
    }

    [Fact]
    public void BuiltIns_ContainsActionItems()
    {
        Assert.Contains(PromptTemplate.BuiltIns, t => t.Name == "Action Items");
    }

    [Fact]
    public void BuiltIns_ContainsDevilsAdvocate()
    {
        Assert.Contains(PromptTemplate.BuiltIns, t => t.Name == "Devil's Advocate");
    }

    [Fact]
    public void BuiltIns_NoTemplateHasNullName()
    {
        Assert.All(PromptTemplate.BuiltIns, t => Assert.NotNull(t.Name));
    }

    [Fact]
    public void BuiltIns_NoTemplateHasEmptyName()
    {
        Assert.All(PromptTemplate.BuiltIns, t => Assert.NotEmpty(t.Name));
    }

    // ── Default ───────────────────────────────────────────────────────────────

    [Fact]
    public void Default_NameIsDefault()
    {
        Assert.Equal("Default", PromptTemplate.Default.Name);
    }

    [Fact]
    public void Default_SystemPromptSuffixIsEmpty()
    {
        Assert.Equal(string.Empty, PromptTemplate.Default.SystemPromptSuffix);
    }

    [Fact]
    public void Default_HasNoMaxTokensOverride()
    {
        Assert.Null(PromptTemplate.Default.MaxTokensOverride);
    }

    // ── Quick Answer ──────────────────────────────────────────────────────────

    [Fact]
    public void QuickAnswer_SuffixMentions80Words()
    {
        var t = PromptTemplate.BuiltIns.First(x => x.Name == "Quick Answer");
        Assert.Contains("80 words", t.SystemPromptSuffix);
    }

    [Fact]
    public void QuickAnswer_HasNoMaxTokensOverride()
    {
        var t = PromptTemplate.BuiltIns.First(x => x.Name == "Quick Answer");
        Assert.Null(t.MaxTokensOverride);
    }

    // ── Detailed Explanation ──────────────────────────────────────────────────

    [Fact]
    public void DetailedExplanation_HasMaxTokensOverride2048()
    {
        var t = PromptTemplate.BuiltIns.First(x => x.Name == "Detailed Explanation");
        Assert.Equal(2048, t.MaxTokensOverride);
    }

    [Fact]
    public void DetailedExplanation_SuffixMentionsDETAILED()
    {
        var t = PromptTemplate.BuiltIns.First(x => x.Name == "Detailed Explanation");
        Assert.Contains("DETAILED", t.SystemPromptSuffix);
    }

    // ── Action Items ──────────────────────────────────────────────────────────

    [Fact]
    public void ActionItems_SuffixMentionsOwner()
    {
        var t = PromptTemplate.BuiltIns.First(x => x.Name == "Action Items");
        Assert.Contains("Owner", t.SystemPromptSuffix);
    }

    // ── Devil's Advocate ──────────────────────────────────────────────────────

    [Fact]
    public void DevilsAdvocate_SuffixMentionsObjections()
    {
        var t = PromptTemplate.BuiltIns.First(x => x.Name == "Devil's Advocate");
        Assert.Contains("objections", t.SystemPromptSuffix, StringComparison.OrdinalIgnoreCase);
    }

    // ── Record equality ───────────────────────────────────────────────────────

    [Fact]
    public void SameValues_AreEqual()
    {
        var a = new PromptTemplate("Test", "suffix");
        var b = new PromptTemplate("Test", "suffix");
        Assert.Equal(a, b);
    }

    [Fact]
    public void DifferentNames_AreNotEqual()
    {
        var a = new PromptTemplate("A", "suffix");
        var b = new PromptTemplate("B", "suffix");
        Assert.NotEqual(a, b);
    }
}

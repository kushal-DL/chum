using Chum.Llm;
using Xunit;

namespace Chum.Tests.Llm;

public sealed class PromptBuilderTests
{
    // ── BuildSystemPrompt — user name ─────────────────────────────────────────

    [Fact]
    public void BuildSystemPrompt_WithUserName_ContainsName()
    {
        var prompt = PromptBuilder.BuildSystemPrompt(userName: "Alice");
        Assert.Contains("Alice", prompt);
    }

    [Fact]
    public void BuildSystemPrompt_NullUserName_FallsBackToTheUser()
    {
        var prompt = PromptBuilder.BuildSystemPrompt(userName: null);
        Assert.Contains("the user", prompt);
    }

    [Fact]
    public void BuildSystemPrompt_WhitespaceUserName_FallsBackToTheUser()
    {
        var prompt = PromptBuilder.BuildSystemPrompt(userName: "   ");
        Assert.Contains("the user", prompt);
    }

    // ── BuildSystemPrompt — platform ──────────────────────────────────────────

    [Fact]
    public void BuildSystemPrompt_WithPlatform_ContainsPlatform()
    {
        var prompt = PromptBuilder.BuildSystemPrompt(platform: "Microsoft Teams");
        Assert.Contains("Microsoft Teams", prompt);
    }

    [Fact]
    public void BuildSystemPrompt_NullPlatform_OmitsPlatformNote()
    {
        var prompt = PromptBuilder.BuildSystemPrompt(platform: null);
        // Should not have "on " followed by a platform name
        Assert.DoesNotContain(" on Microsoft Teams", prompt);
        Assert.DoesNotContain(" on Zoom", prompt);
    }

    // ── BuildSystemPrompt — language note ─────────────────────────────────────

    [Fact]
    public void BuildSystemPrompt_NonEnglishLanguage_ContainsLanguageNote()
    {
        var prompt = PromptBuilder.BuildSystemPrompt(detectedLanguageCode: "fr");
        Assert.Contains("French", prompt);
    }

    [Fact]
    public void BuildSystemPrompt_EnglishLanguage_OmitsLanguageNote()
    {
        var prompt = PromptBuilder.BuildSystemPrompt(detectedLanguageCode: "en");
        Assert.DoesNotContain("Meeting language detected", prompt);
    }

    [Fact]
    public void BuildSystemPrompt_EnglishUS_OmitsLanguageNote()
    {
        var prompt = PromptBuilder.BuildSystemPrompt(detectedLanguageCode: "en-US");
        Assert.DoesNotContain("Meeting language detected", prompt);
    }

    [Fact]
    public void BuildSystemPrompt_NullLanguage_OmitsLanguageNote()
    {
        var prompt = PromptBuilder.BuildSystemPrompt(detectedLanguageCode: null);
        Assert.DoesNotContain("Meeting language detected", prompt);
    }

    [Theory]
    [InlineData("es", "Spanish")]
    [InlineData("fr", "French")]
    [InlineData("de", "German")]
    [InlineData("it", "Italian")]
    [InlineData("pt", "Portuguese")]
    [InlineData("nl", "Dutch")]
    [InlineData("pl", "Polish")]
    [InlineData("ru", "Russian")]
    [InlineData("zh", "Chinese")]
    [InlineData("ja", "Japanese")]
    [InlineData("ko", "Korean")]
    [InlineData("ar", "Arabic")]
    [InlineData("hi", "Hindi")]
    [InlineData("sv", "Swedish")]
    [InlineData("da", "Danish")]
    [InlineData("no", "Norwegian")]
    [InlineData("fi", "Finnish")]
    [InlineData("cs", "Czech")]
    [InlineData("hu", "Hungarian")]
    [InlineData("tr", "Turkish")]
    public void BuildSystemPrompt_KnownLanguageCodes_ProducesCorrectLanguageName(string code, string expected)
    {
        var prompt = PromptBuilder.BuildSystemPrompt(detectedLanguageCode: code);
        Assert.Contains(expected, prompt);
    }

    [Fact]
    public void BuildSystemPrompt_UnknownLanguageCode_FallsBackToUppercaseCode()
    {
        var prompt = PromptBuilder.BuildSystemPrompt(detectedLanguageCode: "xx");
        Assert.Contains("XX", prompt);
    }

    // ── BuildSystemPrompt — template suffix ───────────────────────────────────

    [Fact]
    public void BuildSystemPrompt_WithTemplate_AppendsSuffix()
    {
        var template = new PromptTemplate("Custom", "\n\nCustom suffix text here.");
        var prompt = PromptBuilder.BuildSystemPrompt(template: template);
        Assert.Contains("Custom suffix text here.", prompt);
    }

    [Fact]
    public void BuildSystemPrompt_DefaultTemplate_NoSuffix()
    {
        var prompt = PromptBuilder.BuildSystemPrompt(template: PromptTemplate.Default);
        // Last line of the raw string is the visual queries rule
        Assert.EndsWith("then answer in context of the transcript.", prompt.TrimEnd());
    }

    [Fact]
    public void BuildSystemPrompt_NullTemplate_NoSuffix()
    {
        var prompt = PromptBuilder.BuildSystemPrompt(template: null);
        Assert.EndsWith("then answer in context of the transcript.", prompt.TrimEnd());
    }

    [Fact]
    public void BuildSystemPrompt_QuickAnswerTemplate_Contains80Words()
    {
        var template = PromptTemplate.BuiltIns.First(t => t.Name == "Quick Answer");
        var prompt = PromptBuilder.BuildSystemPrompt(template: template);
        Assert.Contains("80 words", prompt);
    }

    // ── BuildSystemPrompt — always present ────────────────────────────────────

    [Fact]
    public void BuildSystemPrompt_Always_ContainsChum()
    {
        var prompt = PromptBuilder.BuildSystemPrompt();
        Assert.Contains("Chum", prompt);
    }

    [Fact]
    public void BuildSystemPrompt_Always_ContainsCurrentYear()
    {
        var prompt = PromptBuilder.BuildSystemPrompt();
        Assert.Contains(DateTime.Now.Year.ToString(), prompt);
    }

    [Fact]
    public void BuildSystemPrompt_Always_ContainsMeRole()
    {
        var prompt = PromptBuilder.BuildSystemPrompt();
        Assert.Contains("\"Me\"", prompt);
    }

    [Fact]
    public void BuildSystemPrompt_Always_ContainsRemoteRole()
    {
        var prompt = PromptBuilder.BuildSystemPrompt();
        Assert.Contains("\"Remote\"", prompt);
    }

    // ── BuildUserMessage ──────────────────────────────────────────────────────

    [Fact]
    public void BuildUserMessage_WithTranscript_ContainsTranscript()
    {
        var msg = PromptBuilder.BuildUserMessage("Me: Hello world");
        Assert.Contains("Me: Hello world", msg);
    }

    [Fact]
    public void BuildUserMessage_WithTranscript_ContainsInstruction()
    {
        var msg = PromptBuilder.BuildUserMessage("Me: any text");
        Assert.Contains("most recent question", msg);
    }

    [Fact]
    public void BuildUserMessage_EmptyTranscript_ReturnsImageOnlyMessage()
    {
        var msg = PromptBuilder.BuildUserMessage(string.Empty);
        Assert.Contains("No transcript available", msg);
    }

    [Fact]
    public void BuildUserMessage_WhitespaceTranscript_ReturnsImageOnlyMessage()
    {
        var msg = PromptBuilder.BuildUserMessage("   ");
        Assert.Contains("No transcript available", msg);
    }

    [Fact]
    public void BuildUserMessage_WithImage_PrependImageNote()
    {
        var msg = PromptBuilder.BuildUserMessage("Me: show me", hasImage: true);
        Assert.Contains("screenshot", msg);
    }

    [Fact]
    public void BuildUserMessage_NoImage_NoScreenshotMention()
    {
        var msg = PromptBuilder.BuildUserMessage("Me: any text", hasImage: false);
        Assert.DoesNotContain("screenshot", msg);
    }

    [Fact]
    public void BuildUserMessage_EmptyTranscript_WithImage_IncludesImageAndNoTranscript()
    {
        var msg = PromptBuilder.BuildUserMessage(string.Empty, hasImage: true);
        Assert.Contains("screenshot", msg);
        Assert.Contains("No transcript available", msg);
    }
}

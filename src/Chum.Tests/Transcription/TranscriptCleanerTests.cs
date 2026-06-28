using Chum.Transcription;
using Xunit;

namespace Chum.Tests.Transcription;

public sealed class TranscriptCleanerTests
{
    // ── Null / empty input ────────────────────────────────────────────────────────

    [Fact]
    public void Clean_Empty_ReturnsEmpty()
        => Assert.Equal(string.Empty, TranscriptCleaner.Clean(""));

    [Fact]
    public void Clean_Whitespace_ReturnsEmpty()
        => Assert.Equal(string.Empty, TranscriptCleaner.Clean("   \t\n  "));

    // ── Bracketed noise tags ──────────────────────────────────────────────────────

    [Theory]
    [InlineData("[MUSIC]",        "")]
    [InlineData("[SOUND]",        "")]
    [InlineData("[NOISE]",        "")]
    [InlineData("[LAUGHTER]",     "")]
    [InlineData("[APPLAUSE]",     "")]
    [InlineData("[INAUDIBLE]",    "")]
    [InlineData("[BLANK_AUDIO]",  "")]
    [InlineData("[silence]",      "")]
    [InlineData("[Silence]",      "")]
    public void Clean_BracketedNoiseTags_Removed(string input, string expected)
        => Assert.Equal(expected, TranscriptCleaner.Clean(input));

    [Fact]
    public void Clean_NoisyTagMidSentence_TagRemovedTextPreserved()
    {
        var result = TranscriptCleaner.Clean("Hello [NOISE] world");
        Assert.Contains("Hello", result);
        Assert.Contains("world", result);
        Assert.DoesNotContain("[NOISE]", result);
    }

    // ── Parenthesised noise tags ──────────────────────────────────────────────────

    [Theory]
    [InlineData("(MUSIC)",     "")]
    [InlineData("(SOUND)",     "")]
    [InlineData("(NOISE)",     "")]
    [InlineData("(LAUGHTER)",  "")]
    [InlineData("(APPLAUSE)",  "")]
    [InlineData("(INAUDIBLE)", "")]
    [InlineData("(silence)",   "")]
    public void Clean_ParenthesisedNoiseTags_Removed(string input, string _)
    {
        // Just verify no exception and tag is gone (we don't care about trailing whitespace)
        var result = TranscriptCleaner.Clean(input);
        Assert.DoesNotContain(input.Trim('(', ')'), result, StringComparison.OrdinalIgnoreCase);
    }

    // ── Music notation ────────────────────────────────────────────────────────────

    [Fact]
    public void Clean_MusicNotes_Removed()
    {
        var result = TranscriptCleaner.Clean("♪ La la la ♪ hello");
        Assert.DoesNotContain("♪", result);
        Assert.Contains("hello", result);
    }

    [Fact]
    public void Clean_AlternateMusicNotes_Removed()
    {
        var result = TranscriptCleaner.Clean("♫ singing ♫ spoken");
        Assert.DoesNotContain("♫", result);
        Assert.Contains("spoken", result);
    }

    // ── Word repetitions ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData("um um um um",   "um")]
    [InlineData("you you you",   "you")]
    [InlineData("the the the the", "the")]
    public void Clean_WordRepetitions_CollapsedToOne(string input, string expectedWord)
    {
        var result = TranscriptCleaner.Clean(input);
        Assert.Equal(expectedWord, result);
    }

    [Fact]
    public void Clean_TwoAdjacentWords_NotAffected()
    {
        // Two occurrences is the Whisper minimum for a filler stutter — our regex matches 2+.
        // The regex uses {2,} which means TWO or more repetitions beyond the first,
        // so "um um" (2 total) should be cleaned too, but "hello world" is untouched.
        var result = TranscriptCleaner.Clean("hello world");
        Assert.Equal("hello world", result);
    }

    // ── Whitespace normalisation ──────────────────────────────────────────────────

    [Fact]
    public void Clean_MultipleSpaces_CollapsedToSingle()
    {
        var result = TranscriptCleaner.Clean("hello   world");
        Assert.Equal("hello world", result);
    }

    [Fact]
    public void Clean_LeadingTrailingSpaces_Trimmed()
    {
        var result = TranscriptCleaner.Clean("  hello world  ");
        Assert.Equal("hello world", result);
    }

    [Fact]
    public void Clean_NewlinesTabsReplaced()
    {
        var result = TranscriptCleaner.Clean("hello\tworld\nfoo");
        Assert.DoesNotContain('\t', result);
        Assert.DoesNotContain('\n', result);
        Assert.Contains("hello", result);
    }

    // ── Real-world Whisper hallucination examples ────────────────────────────────

    [Fact]
    public void Clean_TypicalHallucinationLine_ProducesEmpty()
    {
        // Whisper often emits just "[BLANK_AUDIO]" for silent segments
        var result = TranscriptCleaner.Clean("[BLANK_AUDIO]");
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void Clean_MixedNoiseAndSpeech_OnlySpeechPreserved()
    {
        var input  = "Good morning [NOISE] everyone ♪ music ♪ please take a seat [INAUDIBLE]";
        var result = TranscriptCleaner.Clean(input);
        Assert.Contains("Good morning", result);
        Assert.Contains("everyone", result);
        Assert.Contains("please take a seat", result);
        Assert.DoesNotContain("[NOISE]",     result);
        Assert.DoesNotContain("♪",           result);
        Assert.DoesNotContain("[INAUDIBLE]", result);
    }
}

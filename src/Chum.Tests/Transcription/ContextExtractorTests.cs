using Chum.Audio.Models;
using Chum.Transcription;
using Chum.Transcription.Models;
using Xunit;

namespace Chum.Tests.Transcription;

public sealed class ContextExtractorTests
{
    private static TranscriptSegment Seg(string text, DateTimeOffset time, AudioSource source = AudioSource.Microphone)
        => new(time, source, text);

    private static (TranscriptBuffer buf, ContextExtractor ext) Make()
    {
        var buf = new TranscriptBuffer();
        return (buf, new ContextExtractor(buf));
    }

    // ── Empty buffer ──────────────────────────────────────────────────────────────

    [Fact]
    public void BuildContext_EmptyBuffer_ReturnsEmpty()
    {
        var (_, ext) = Make();
        Assert.Equal(string.Empty, ext.BuildContext(DateTimeOffset.UtcNow));
    }

    // ── Output format ─────────────────────────────────────────────────────────────

    [Fact]
    public void BuildContext_IncludesTimestampAndSpeakerAndText()
    {
        var (buf, ext) = Make();
        var t = DateTimeOffset.UtcNow;
        // AudioSource.Microphone → SpeakerLabel "Me"
        buf.Add(Seg("hello world", t, AudioSource.Microphone));

        var ctx = ext.BuildContext(t.AddSeconds(5));
        Assert.Contains("Me", ctx);
        Assert.Contains("hello world", ctx);
        // Timestamp formatted as HH:mm:ss
        Assert.Contains(t.ToString("HH:mm:ss"), ctx);
    }

    // ── Recent-only (no section headers) ─────────────────────────────────────────

    [Fact]
    public void BuildContext_OnlyRecentSegments_NoSectionHeaders()
    {
        var (buf, ext) = Make();
        var now = DateTimeOffset.UtcNow;
        // All segments within the 30-second recent window
        buf.Add(Seg("a", now.AddSeconds(-20)));
        buf.Add(Seg("b", now.AddSeconds(-10)));

        var ctx = ext.BuildContext(now);
        Assert.DoesNotContain("[Earlier in the meeting]", ctx);
        Assert.DoesNotContain("[Recent]", ctx);
    }

    // ── Older + recent (section headers present) ──────────────────────────────────

    [Fact]
    public void BuildContext_OlderAndRecentSegments_HasSectionHeaders()
    {
        var (buf, ext) = Make();
        var now = DateTimeOffset.UtcNow;
        buf.Add(Seg("old",    now.AddMinutes(-5)));   // older
        buf.Add(Seg("recent", now.AddSeconds(-10)));  // recent

        var ctx = ext.BuildContext(now);
        Assert.Contains("[Earlier in the meeting]", ctx);
        Assert.Contains("[Recent]", ctx);
        Assert.Contains("old",    ctx);
        Assert.Contains("recent", ctx);
    }

    [Fact]
    public void BuildContext_OlderBeforeRecent_OrderPreserved()
    {
        var (buf, ext) = Make();
        var now = DateTimeOffset.UtcNow;
        buf.Add(Seg("older-text",  now.AddMinutes(-5)));
        buf.Add(Seg("recent-text", now.AddSeconds(-5)));

        var ctx = ext.BuildContext(now);
        int olderIdx  = ctx.IndexOf("older-text",  StringComparison.Ordinal);
        int recentIdx = ctx.IndexOf("recent-text", StringComparison.Ordinal);
        Assert.True(olderIdx < recentIdx, "Older segment must appear before recent segment");
    }

    // ── Token budget ─────────────────────────────────────────────────────────────

    [Fact]
    public void BuildContext_TinyBudget_OnlyRecentIncluded()
    {
        var (buf, ext) = Make();
        var now = DateTimeOffset.UtcNow;
        // Add many older segments
        for (int i = 0; i < 20; i++)
            buf.Add(Seg(new string('x', 200), now.AddMinutes(-(20 - i))));
        buf.Add(Seg("recent-must-appear", now.AddSeconds(-5)));

        // Budget so tight that only recent fits (budget = 200 tokens = 800 chars)
        var ctx = ext.BuildContext(now, tokenBudget: 200);
        Assert.Contains("recent-must-appear", ctx);
    }

    [Fact]
    public void BuildContext_ZeroBudget_StillReturnsRecent()
    {
        // Recent segments are always included regardless of budget
        var (buf, ext) = Make();
        var now = DateTimeOffset.UtcNow;
        buf.Add(Seg("must-be-here", now.AddSeconds(-5)));

        var ctx = ext.BuildContext(now, tokenBudget: 0);
        Assert.Contains("must-be-here", ctx);
    }

    // ── Boundary: segment exactly at the 30-second boundary ──────────────────────

    [Fact]
    public void BuildContext_SegmentAtExact30sWindow_IncludedInRecent()
    {
        var (buf, ext) = Make();
        var now = DateTimeOffset.UtcNow;
        var boundary = now.AddSeconds(-30); // exactly at the boundary
        buf.Add(Seg("boundary-seg", boundary));

        var ctx = ext.BuildContext(now);
        Assert.Contains("boundary-seg", ctx);
    }

    // ── Multiple segments same speaker ────────────────────────────────────────────

    [Fact]
    public void BuildContext_MultipleSegments_AllIncluded()
    {
        var (buf, ext) = Make();
        var now = DateTimeOffset.UtcNow;
        buf.Add(Seg("first",  now.AddSeconds(-25)));
        buf.Add(Seg("second", now.AddSeconds(-15)));
        buf.Add(Seg("third",  now.AddSeconds(-5)));

        var ctx = ext.BuildContext(now);
        Assert.Contains("first",  ctx);
        Assert.Contains("second", ctx);
        Assert.Contains("third",  ctx);
    }
}

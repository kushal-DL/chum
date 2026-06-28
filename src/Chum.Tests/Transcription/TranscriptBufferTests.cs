using Chum.Audio.Models;
using Chum.Transcription;
using Chum.Transcription.Models;
using Xunit;

namespace Chum.Tests.Transcription;

public sealed class TranscriptBufferTests
{
    private static TranscriptSegment Seg(string text, DateTimeOffset time, AudioSource source = AudioSource.Microphone)
        => new(time, source, text);

    // ── Add / GetAll ──────────────────────────────────────────────────────────────

    [Fact]
    public void GetAll_Empty_ReturnsEmpty()
    {
        var buf = new TranscriptBuffer();
        Assert.Empty(buf.GetAll());
    }

    [Fact]
    public void GetAll_PreservesInsertionOrder()
    {
        var buf = new TranscriptBuffer();
        var t0 = DateTimeOffset.UtcNow;
        buf.Add(Seg("first",  t0));
        buf.Add(Seg("second", t0.AddSeconds(1)));
        buf.Add(Seg("third",  t0.AddSeconds(2)));

        var all = buf.GetAll();
        Assert.Equal(3, all.Count);
        Assert.Equal("first",  all[0].Text);
        Assert.Equal("second", all[1].Text);
        Assert.Equal("third",  all[2].Text);
    }

    [Fact]
    public void Count_ReflectsAdded()
    {
        var buf = new TranscriptBuffer();
        Assert.Equal(0, buf.Count);
        buf.Add(Seg("a", DateTimeOffset.UtcNow));
        Assert.Equal(1, buf.Count);
        buf.Add(Seg("b", DateTimeOffset.UtcNow));
        Assert.Equal(2, buf.Count);
    }

    // ── GetSince ──────────────────────────────────────────────────────────────────

    [Fact]
    public void GetSince_ReturnsOnlyNewerSegments()
    {
        var buf = new TranscriptBuffer();
        var t0 = DateTimeOffset.UtcNow;
        buf.Add(Seg("old",    t0));
        buf.Add(Seg("middle", t0.AddSeconds(5)));
        buf.Add(Seg("new",    t0.AddSeconds(10)));

        var since = buf.GetSince(t0.AddSeconds(5));
        Assert.Equal(2, since.Count);
        Assert.Equal("middle", since[0].Text);
        Assert.Equal("new",    since[1].Text);
    }

    [Fact]
    public void GetSince_FutureTimestamp_ReturnsEmpty()
    {
        var buf = new TranscriptBuffer();
        buf.Add(Seg("a", DateTimeOffset.UtcNow));
        var since = buf.GetSince(DateTimeOffset.UtcNow.AddHours(1));
        Assert.Empty(since);
    }

    // ── GetRecent ─────────────────────────────────────────────────────────────────

    [Fact]
    public void GetRecent_LimitsToCount()
    {
        var buf = new TranscriptBuffer();
        var t0 = DateTimeOffset.UtcNow;
        for (int i = 0; i < 10; i++)
            buf.Add(Seg($"seg{i}", t0.AddSeconds(i)));

        var recent = buf.GetRecent(3);
        Assert.Equal(3, recent.Count);
        Assert.Equal("seg7", recent[0].Text);
        Assert.Equal("seg8", recent[1].Text);
        Assert.Equal("seg9", recent[2].Text);
    }

    [Fact]
    public void GetRecent_CountExceedsBuffer_ReturnsAll()
    {
        var buf = new TranscriptBuffer();
        buf.Add(Seg("a", DateTimeOffset.UtcNow));
        buf.Add(Seg("b", DateTimeOffset.UtcNow.AddSeconds(1)));

        var recent = buf.GetRecent(100);
        Assert.Equal(2, recent.Count);
    }

    [Fact]
    public void GetRecent_Zero_ReturnsEmpty()
    {
        var buf = new TranscriptBuffer();
        buf.Add(Seg("a", DateTimeOffset.UtcNow));
        Assert.Empty(buf.GetRecent(0));
    }

    // ── Clear ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void Clear_EmptiesBuffer()
    {
        var buf = new TranscriptBuffer();
        buf.Add(Seg("a", DateTimeOffset.UtcNow));
        buf.Clear();
        Assert.Equal(0, buf.Count);
        Assert.Empty(buf.GetAll());
    }

    // ── Retention / eviction ─────────────────────────────────────────────────────

    [Fact]
    public void Eviction_OldSegmentsPurgedOnAdd()
    {
        // Use a 5-second retention window; add a segment 10 s in the past.
        var buf = new TranscriptBuffer(TimeSpan.FromSeconds(5));
        var old = DateTimeOffset.UtcNow.AddSeconds(-10);
        buf.Add(Seg("stale", old));

        // Trigger eviction by adding a fresh segment
        buf.Add(Seg("fresh", DateTimeOffset.UtcNow));

        var all = buf.GetAll();
        Assert.DoesNotContain(all, s => s.Text == "stale");
        Assert.Single(all, s => s.Text == "fresh");
    }

    [Fact]
    public void SetRetentionWindow_AffectsSubsequentEviction()
    {
        var buf = new TranscriptBuffer(TimeSpan.FromHours(1));
        var old = DateTimeOffset.UtcNow.AddMinutes(-30);
        buf.Add(Seg("will-survive-initially", old));
        Assert.Equal(1, buf.Count);

        // Tighten the window so the existing segment is now stale
        buf.SetRetentionWindow(TimeSpan.FromMinutes(1));
        buf.Add(Seg("trigger-eviction", DateTimeOffset.UtcNow));

        Assert.Equal(1, buf.Count);
        Assert.Equal("trigger-eviction", buf.GetAll()[0].Text);
    }

    // ── Thread safety ─────────────────────────────────────────────────────────────

    [Fact]
    public void ConcurrentAdds_DoNotCorruptCount()
    {
        var buf = new TranscriptBuffer();
        var t0 = DateTimeOffset.UtcNow;
        const int threads = 8, addsPerThread = 50;

        var tasks = Enumerable.Range(0, threads).Select(i => Task.Run(() =>
        {
            for (int j = 0; j < addsPerThread; j++)
                buf.Add(Seg($"t{i}s{j}", t0.AddMilliseconds(i * 1000 + j)));
        })).ToArray();
        Task.WaitAll(tasks);

        Assert.Equal(threads * addsPerThread, buf.Count);
    }
}

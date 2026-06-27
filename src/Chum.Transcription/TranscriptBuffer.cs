using Chum.Transcription.Models;

namespace Chum.Transcription;

/// <summary>
/// Thread-safe rolling transcript store.
/// Evicts segments older than the configured retention window.
/// </summary>
public sealed class TranscriptBuffer
{
    private readonly LinkedList<TranscriptSegment> _segments = new();
    private readonly Lock _lock = new();
    private TimeSpan _retentionWindow;

    public TranscriptBuffer(TimeSpan? retentionWindow = null)
    {
        _retentionWindow = retentionWindow ?? TimeSpan.FromMinutes(10);
    }

    public void SetRetentionWindow(TimeSpan window)
    {
        lock (_lock) { _retentionWindow = window; }
    }

    public void Add(TranscriptSegment segment)
    {
        lock (_lock)
        {
            _segments.AddLast(segment);
            Evict();
        }
    }

    /// <summary>Returns all segments since <paramref name="since"/>, oldest first.</summary>
    public IReadOnlyList<TranscriptSegment> GetSince(DateTimeOffset since)
    {
        lock (_lock)
        {
            var result = new List<TranscriptSegment>();
            for (var node = _segments.Last; node is not null; node = node.Previous)
            {
                if (node.Value.Timestamp < since) break;
                result.Insert(0, node.Value);
            }
            return result;
        }
    }

    /// <summary>Returns the most recent <paramref name="count"/> segments.</summary>
    public IReadOnlyList<TranscriptSegment> GetRecent(int count)
    {
        lock (_lock)
        {
            var result = new List<TranscriptSegment>(count);
            var node = _segments.Last;
            while (node is not null && result.Count < count)
            {
                result.Insert(0, node.Value);
                node = node.Previous;
            }
            return result;
        }
    }

    /// <summary>Returns all segments in the buffer, oldest first.</summary>
    public IReadOnlyList<TranscriptSegment> GetAll()
    {
        lock (_lock) { return [.. _segments]; }
    }

    public int Count { get { lock (_lock) return _segments.Count; } }

    public void Clear()
    {
        lock (_lock) { _segments.Clear(); }
    }

    private void Evict()
    {
        var cutoff = DateTimeOffset.UtcNow - _retentionWindow;
        while (_segments.First is not null && _segments.First.Value.Timestamp < cutoff)
            _segments.RemoveFirst();
    }
}

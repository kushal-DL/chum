using System.Text;
using Chum.Transcription.Models;

namespace Chum.Transcription;

/// <summary>
/// Builds the transcript context string sent to the LLM.
/// Respects a token budget: estimates ~4 chars/token; trims oldest segments when over budget.
/// Always preserves the 30 s immediately before the query trigger.
/// </summary>
public sealed class ContextExtractor(TranscriptBuffer buffer)
{
    private const int DefaultTokenBudget = 8_000;
    private const int CharsPerToken = 4;
    private const int ImmediateWindowSeconds = 30;

    /// <summary>
    /// Returns formatted transcript context for the LLM.
    /// </summary>
    /// <param name="queryTime">Time the hotkey was pressed (end of context window).</param>
    /// <param name="tokenBudget">Max tokens to use for transcript context.</param>
    public string BuildContext(DateTimeOffset queryTime, int tokenBudget = DefaultTokenBudget)
    {
        var all = buffer.GetAll();
        if (all.Count == 0) return string.Empty;

        var sb = new StringBuilder();
        int charBudget = tokenBudget * CharsPerToken;
        var immediateStart = queryTime.AddSeconds(-ImmediateWindowSeconds);

        // Separate into "recent" (always include) and "older" (include if budget allows)
        var recent = all.Where(s => s.Timestamp >= immediateStart).ToList();
        var older = all.Where(s => s.Timestamp < immediateStart).ToList();

        // Build recent section first (guaranteed to be included)
        var recentText = FormatSegments(recent);

        // Fill remaining budget with older segments (newest first, then reverse)
        int recentChars = recentText.Length;
        int olderBudget = charBudget - recentChars;

        var olderLines = new List<string>();
        for (int i = older.Count - 1; i >= 0 && olderBudget > 0; i--)
        {
            string line = FormatSegment(older[i]);
            if (line.Length > olderBudget) break;
            olderLines.Insert(0, line);
            olderBudget -= line.Length;
        }

        if (olderLines.Count > 0)
        {
            sb.AppendLine("[Earlier in the meeting]");
            foreach (var line in olderLines) sb.AppendLine(line);
            sb.AppendLine("[Recent]");
        }

        sb.Append(recentText);
        return sb.ToString().TrimEnd();
    }

    private static string FormatSegments(IEnumerable<TranscriptSegment> segments)
    {
        var sb = new StringBuilder();
        foreach (var seg in segments) sb.AppendLine(FormatSegment(seg));
        return sb.ToString();
    }

    private static string FormatSegment(TranscriptSegment seg)
        => $"[{seg.Timestamp:HH:mm:ss}] {seg.SpeakerLabel}: \"{seg.Text}\"";
}

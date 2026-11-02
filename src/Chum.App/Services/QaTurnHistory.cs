namespace Chum.App.Services;

using Chum.App.Models;

/// <summary>
/// Maintains a clean FIFO buffer of explicit Q&amp;A turns between the user and assistant.
/// Eliminates noise from ambient background transcriptions and provides multi-turn continuity.
/// </summary>
public sealed class QaTurnHistory
{
    private readonly int _maxTurns;
    private readonly List<QaTurn> _turns = [];
    private readonly object _lock = new();

    public QaTurnHistory(int maxTurns = 5)
    {
        _maxTurns = Math.Max(1, maxTurns);
    }

    public void AddTurn(string question, string answer, string category, ResponseModeType mode)
    {
        if (string.IsNullOrWhiteSpace(question) && string.IsNullOrWhiteSpace(answer))
            return;

        lock (_lock)
        {
            _turns.Add(new QaTurn(
                question.Trim(),
                answer.Trim(),
                string.IsNullOrWhiteSpace(category) ? "others" : category.Trim().ToLowerInvariant(),
                mode,
                DateTimeOffset.UtcNow));

            while (_turns.Count > _maxTurns)
                _turns.RemoveAt(0);
        }
    }

    public IReadOnlyList<QaTurn> GetRecentTurns(int count = 3)
    {
        lock (_lock)
        {
            int skip = Math.Max(0, _turns.Count - count);
            return _turns.Skip(skip).ToList();
        }
    }

    public string BuildContextPrompt(int count = 2)
    {
        var turns = GetRecentTurns(count);
        if (turns.Count == 0) return string.Empty;

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("Recent Conversation Turns:");
        foreach (var t in turns)
        {
            sb.AppendLine($"User: {t.Question}");
            sb.AppendLine($"Assistant: {t.Answer}");
        }
        return sb.ToString().TrimEnd();
    }

    public void Clear()
    {
        lock (_lock) { _turns.Clear(); }
    }
}

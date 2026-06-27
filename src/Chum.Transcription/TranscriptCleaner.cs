using System.Text.RegularExpressions;

namespace Chum.Transcription;

/// <summary>
/// Post-processes raw Whisper output to remove noise artefacts and reduce hallucination content
/// before the transcript reaches the LLM context window.
/// </summary>
public static partial class TranscriptCleaner
{
    // Whisper commonly emits these when audio contains music, silence, or background noise.
    // Case-insensitive full-segment matches are rejected by IsHallucination; partial-match patterns
    // handled here strip bracketed/parenthesized noise tags from otherwise real speech.
    private static readonly Regex NoiseTags = NoisTagPattern();

    // Consecutive word repetitions: "um um um" → "um", "you you you you" → "you"
    // Matches 2+ adjacent occurrences of the same word (word boundary aware).
    private static readonly Regex WordRepeat = WordRepeatPattern();

    // Music/lyric lines surrounded by musical notes (♪ ... ♪ or ♫ ... ♫)
    private static readonly Regex MusicNote = MusicNotePattern();

    public static string Clean(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;

        // 1. Remove bracketed/parenthesized noise tags embedded in speech
        text = NoiseTags.Replace(text, " ");

        // 2. Remove music notation lines
        text = MusicNote.Replace(text, " ");

        // 3. Reduce word-level repetitions (covers filler stuttering and Whisper looping)
        text = WordRepeat.Replace(text, "$1");

        // 4. Collapse multiple spaces → single space; trim
        text = CollapseSpaces(text);

        return text;
    }

    private static string CollapseSpaces(string text)
    {
        var span = text.AsSpan().Trim();
        var sb = new System.Text.StringBuilder(span.Length);
        bool lastWasSpace = false;
        foreach (var c in span)
        {
            if (c == ' ' || c == '\t' || c == '\r' || c == '\n')
            {
                if (!lastWasSpace) sb.Append(' ');
                lastWasSpace = true;
            }
            else
            {
                sb.Append(c);
                lastWasSpace = false;
            }
        }
        return sb.ToString();
    }

    [GeneratedRegex(
        @"\[(?:MUSIC|SOUND|NOISE|LAUGHTER|APPLAUSE|INAUDIBLE|BLANK_AUDIO|silence|Silence)\]" +
        @"|\((?:MUSIC|SOUND|NOISE|LAUGHTER|APPLAUSE|INAUDIBLE|silence)\)" +
        @"|♪[^♪]*♪|♫[^♫]*♫",
        RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex NoisTagPattern();

    [GeneratedRegex(
        @"\b(\w+)(?:\s+\1){2,}\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex WordRepeatPattern();

    [GeneratedRegex(
        @"♪.*?♪|♫.*?♫",
        RegexOptions.Compiled)]
    private static partial Regex MusicNotePattern();
}

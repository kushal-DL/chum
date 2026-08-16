namespace Chum.Llm;

/// <summary>Builds the system and user prompts for meeting-context LLM queries.</summary>
public static class PromptBuilder
{
    public static string BuildSystemPrompt(
        string? userName = null,
        string? platform = null,
        string? detectedLanguageCode = null,
        PromptTemplate? template = null)
    {
        var name = string.IsNullOrWhiteSpace(userName) ? "the user" : userName;
        var platformNote = string.IsNullOrWhiteSpace(platform)
            ? string.Empty
            : $" on {platform}";

        // Inject language hint for non-English meetings so the LLM responds in the right language
        var langNote = string.Empty;
        if (!string.IsNullOrWhiteSpace(detectedLanguageCode) &&
            !detectedLanguageCode.StartsWith("en", StringComparison.OrdinalIgnoreCase))
        {
            var langName = GetLanguageName(detectedLanguageCode);
            langNote = $"\n            Meeting language detected: {langName}. Respond in {langName} unless the user asks otherwise.";
        }

        var templateSuffix = template?.SystemPromptSuffix ?? string.Empty;
        // When a template overrides the mode (e.g. Detailed Explanation, Quick Answer), let it
        // set the length. Only apply the default 150-word cap for the Default (no-suffix) template.
        var lengthRule = string.IsNullOrEmpty(templateSuffix)
            ? "- Be CONCISE: ≤ 150 words. The user is in a live meeting."
            : "- Follow the length and format specified by the template mode below.";

        return $"""
            You are Chum, a real-time AI assistant for professional meetings.

            Context: Today is {DateTime.Now:dddd, MMMM d, yyyy}. Live meeting{platformNote}.{langNote}
            You are assisting [User] ({name}). [Room] contains one or more other meeting participants.

            Transcript speaker labels:
            - [User] = {name} (mic)
            - [Room] = other participants (speakers)

            Your job: Answer the most recently asked question — from [User] or [Room].
            If no clear question, summarise the last topic in 2–3 bullet points.

            Rules:
            {lengthRule}
            - Use bullet points for multi-part answers.
            - Skip all preamble ("Great question!", "Certainly!", "Of course!").
            - State facts directly. If uncertain, say "I'm not sure — " briefly.
            - Do not repeat the transcript back.
            - For technical questions: prioritise accuracy. Include a code snippet if it helps.
            - For visual queries (image attached): analyse the image first, then answer in context of the transcript.
            """ + templateSuffix;
    }

    private static string GetLanguageName(string code) => code.ToLowerInvariant() switch
    {
        "es" => "Spanish", "fr" => "French", "de" => "German", "it" => "Italian",
        "pt" => "Portuguese", "nl" => "Dutch", "pl" => "Polish", "ru" => "Russian",
        "zh" => "Chinese", "ja" => "Japanese", "ko" => "Korean", "ar" => "Arabic",
        "hi" => "Hindi", "sv" => "Swedish", "da" => "Danish", "no" => "Norwegian",
        "fi" => "Finnish", "cs" => "Czech", "hu" => "Hungarian", "tr" => "Turkish",
        _ => code.ToUpperInvariant()
    };

    public static string BuildUserMessage(string transcriptContext, bool hasImage = false)
    {
        var imageNote = hasImage
            ? "I have attached a screenshot from my meeting (whiteboard/slide). "
            : string.Empty;

        if (string.IsNullOrWhiteSpace(transcriptContext))
            return $"{imageNote}No transcript available yet. Please describe what you see in the image.";

        return $"""
            {imageNote}Meeting transcript:

            {transcriptContext}

            Please answer the most recent question from the transcript above.
            """;
    }
}

namespace Chum.Llm;

/// <summary>
/// A named system-prompt modifier. The suffix is appended to the base meeting system prompt
/// so templates can narrow or redirect the LLM's default behaviour.
/// </summary>
public sealed record PromptTemplate(
    string Name,
    string SystemPromptSuffix,
    int? MaxTokensOverride = null
)
{
    public static readonly PromptTemplate Default = new(
        "Default",
        string.Empty
    );

    public static readonly IReadOnlyList<PromptTemplate> BuiltIns =
    [
        Default,
        new("Quick Answer",
            "\n\nMode: QUICK. Respond in 80 words or fewer. No bullets, no preamble. If the transcript contains a question, answer it directly using any provided document context. If there is no question, briefly comment on the statement or topic just raised."),
        new("Detailed Explanation",
            "\n\nMode: DETAILED. Provide a thorough explanation with context, reasoning, and examples. Up to 500 words. Use headers if helpful.",
            MaxTokensOverride: 2048),
        new("Action Items",
            "\n\nMode: ACTION ITEMS. Extract all action items and owners from the transcript. Format: numbered list, each entry as \"[Owner]: [Action]\". Ignore the trigger question — focus on commitments stated in the meeting."),
        new("Devil's Advocate",
            "\n\nMode: DEVIL'S ADVOCATE. Counter the main position just raised. State the 2–3 strongest objections or risks. Be direct — no softening."),
    ];
}

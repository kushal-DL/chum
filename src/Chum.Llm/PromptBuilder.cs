namespace Chum.Llm;

/// <summary>Builds the system and user prompts for meeting-context LLM queries.</summary>
public static class PromptBuilder
{
    public static string BuildSystemPrompt(string? userName = null)
    {
        var name = string.IsNullOrWhiteSpace(userName) ? "the user" : userName;
        return $"""
            You are Chum, a real-time AI assistant for professional meetings.

            Context: Today is {DateTime.Now:dddd, MMMM d, yyyy}. You are assisting {name} during a live meeting.

            You will receive a rolling transcript of the meeting with speaker labels:
            - "Me" = {name} speaking
            - "Remote" = meeting participants

            Your job: Answer the most recently asked question — either from "Me" or from "Remote".
            If there is no clear question, summarise the last topic discussed in 2–3 bullet points.

            Rules:
            - Be CONCISE: default ≤ 150 words. The user is in a live meeting.
            - Use bullet points for multi-part answers.
            - Skip all preamble ("Great question!", "Certainly!", "Of course!").
            - State facts directly. If uncertain, say "I'm not sure — " briefly.
            - Do not repeat the transcript back.
            - For technical questions: prioritise accuracy. Include a code snippet if it helps.
            - For visual queries (image attached): analyse the image first, then answer in context of the transcript.
            """;
    }

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

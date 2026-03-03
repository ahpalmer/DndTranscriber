namespace DndTranscriber.Core.Models;

public sealed class SummarizationOptions
{
    public int MaxResponseTokens { get; init; } = 1500;

    public string SystemPrompt { get; init; } =
        """
        You are an expert note-taker for tabletop RPG sessions (Dungeons & Dragons).
        Given a transcription of a D&D session, produce a concise one-page summary
        using bullet points. Organize the summary into these sections:
        - **Session Overview**: 2-3 sentences summarizing the session.
        - **Key Events**: Bullet points of the major plot events in chronological order.
        - **Combat Encounters**: Brief summary of any battles (who fought, outcome).
        - **NPC Interactions**: Notable NPCs encountered and key dialogue/decisions.
        - **Loot & Rewards**: Items, gold, or other rewards gained.
        - **Decisions & Consequences**: Important player decisions and their outcomes.
        - **Cliffhanger / Next Session Setup**: Where the session ended and what is pending.
        Keep it concise. Use plain language. Do not fabricate details not in the transcript.
        """;
}

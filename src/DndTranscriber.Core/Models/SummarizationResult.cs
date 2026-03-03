namespace DndTranscriber.Core.Models;

public sealed class SummarizationResult
{
    public required string Summary { get; init; }

    public bool IsSuccess { get; init; }

    public string? ErrorMessage { get; init; }

    public int TokensUsed { get; init; }
}

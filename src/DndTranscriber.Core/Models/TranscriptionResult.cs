namespace DndTranscriber.Core.Models;

public sealed class TranscriptionResult
{
    public required string Text { get; init; }

    public TimeSpan Duration { get; init; }

    public bool IsSuccess { get; init; }

    public string? ErrorMessage { get; init; }
}

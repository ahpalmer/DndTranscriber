namespace DndTranscriber.Core.Models;

public sealed class AudioChunk
{
    public int Index { get; init; }

    public required string FilePath { get; init; }

    public TimeSpan Duration { get; init; }

    public TimeSpan StartOffset { get; init; }
}

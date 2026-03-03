namespace DndTranscriber.Core.Models;

public sealed class TranscriptionOptions
{
    public string Locale { get; init; } = "en-US";

    public bool EnableDiarization { get; init; } = false;

    public TimeSpan MaxChunkDuration { get; init; } = TimeSpan.FromHours(24);
}

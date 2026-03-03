namespace DndTranscriber.DataAccess.Configuration;

public sealed class AzureSpeechSettings
{
    public const string SectionName = "AzureSpeech";

    public required string SpeechApiKey { get; init; }

    public required string Region { get; init; }

    public int TimeToLiveHours { get; init; } = 48;

    public int PollingIntervalSeconds { get; init; } = 30;

    public int MaxWaitTimeMinutes { get; init; } = 120;
}

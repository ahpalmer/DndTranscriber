namespace DndTranscriber.DataAccess.Transcription;

using System.Text.Json.Serialization;

internal sealed class BatchTranscriptionRequest
{
    [JsonPropertyName("contentUrls")]
    public required string[] ContentUrls { get; init; }

    [JsonPropertyName("locale")]
    public required string Locale { get; init; }

    [JsonPropertyName("displayName")]
    public required string DisplayName { get; init; }

    [JsonPropertyName("properties")]
    public required BatchTranscriptionProperties Properties { get; init; }
}

internal sealed class BatchTranscriptionProperties
{
    [JsonPropertyName("wordLevelTimestampsEnabled")]
    public bool WordLevelTimestampsEnabled { get; init; }

    [JsonPropertyName("diarizationEnabled")]
    public bool DiarizationEnabled { get; init; }

    [JsonPropertyName("timeToLiveHours")]
    public int TimeToLiveHours { get; init; } = 48;
}

internal sealed class BatchTranscriptionResponse
{
    [JsonPropertyName("self")]
    public required string Self { get; init; }

    [JsonPropertyName("status")]
    public required string Status { get; init; }

    [JsonPropertyName("links")]
    public BatchTranscriptionLinks? Links { get; init; }
}

internal sealed class BatchTranscriptionLinks
{
    [JsonPropertyName("files")]
    public string? Files { get; init; }
}

internal sealed class TranscriptionFilesResponse
{
    [JsonPropertyName("values")]
    public required TranscriptionFileEntry[] Values { get; init; }
}

internal sealed class TranscriptionFileEntry
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("kind")]
    public required string Kind { get; init; }

    [JsonPropertyName("links")]
    public required TranscriptionFileLinks Links { get; init; }
}

internal sealed class TranscriptionFileLinks
{
    [JsonPropertyName("contentUrl")]
    public required string ContentUrl { get; init; }
}

internal sealed class TranscriptionResultFile
{
    [JsonPropertyName("combinedRecognizedPhrases")]
    public CombinedRecognizedPhrase[]? CombinedRecognizedPhrases { get; init; }
}

internal sealed class CombinedRecognizedPhrase
{
    [JsonPropertyName("channel")]
    public int Channel { get; init; }

    [JsonPropertyName("display")]
    public required string Display { get; init; }
}

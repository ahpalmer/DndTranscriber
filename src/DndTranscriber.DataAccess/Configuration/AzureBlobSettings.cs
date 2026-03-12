namespace DndTranscriber.DataAccess.Configuration;

public sealed class AzureBlobSettings
{
    public const string SectionName = "AzureBlob";

    public required string ConnectionString { get; init; }

    public string ContainerName { get; init; } = "dndtranscriber-audio";

    public int SasTokenExpiryHours { get; init; } = 24;
}

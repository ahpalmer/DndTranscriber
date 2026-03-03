namespace DndTranscriber.DataAccess.Configuration;

public sealed class AnthropicSettings
{
    public const string SectionName = "Anthropic";

    public required string AnthropicApiKey { get; init; }

    public string Model { get; init; } = "claude-sonnet-4-20250514";
}

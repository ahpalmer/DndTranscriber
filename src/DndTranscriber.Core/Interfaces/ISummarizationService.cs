namespace DndTranscriber.Core.Interfaces;

using DndTranscriber.Core.Models;

public interface ISummarizationService
{
    Task<SummarizationResult> SummarizeAsync(
        string fullText,
        SummarizationOptions options,
        CancellationToken cancellationToken = default);
}

namespace DndTranscriber.Core.Interfaces;

using DndTranscriber.Core.Models;

public interface ITranscriptionService
{
    Task<TranscriptionResult> TranscribeAsync(
        string audioFilePath,
        TranscriptionOptions options,
        CancellationToken cancellationToken = default);
}

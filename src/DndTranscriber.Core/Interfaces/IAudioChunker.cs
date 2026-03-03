namespace DndTranscriber.Core.Interfaces;

using DndTranscriber.Core.Models;

public interface IAudioChunker
{
    bool NeedsChunking(string audioFilePath, TimeSpan maxChunkDuration);

    Task<IReadOnlyList<AudioChunk>> SplitAsync(
        string audioFilePath,
        TimeSpan maxChunkDuration,
        string outputDirectory,
        CancellationToken cancellationToken = default);
}

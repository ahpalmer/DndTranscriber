namespace DndTranscriber.DataAccess.Audio;

using DndTranscriber.Core.Interfaces;
using DndTranscriber.Core.Models;
using FFMpegCore;
using Microsoft.Extensions.Logging;

public sealed class FFMpegAudioChunker : IAudioChunker
{
    private readonly ILogger<FFMpegAudioChunker> _logger;

    public FFMpegAudioChunker(ILogger<FFMpegAudioChunker> logger)
    {
        _logger = logger;
        EnsureFFMpegInstalled();
    }

    public bool NeedsChunking(string audioFilePath, TimeSpan maxChunkDuration)
    {
        var mediaInfo = FFProbe.Analyse(audioFilePath);
        return mediaInfo.Duration > maxChunkDuration;
    }

    public async Task<IReadOnlyList<AudioChunk>> SplitAsync(
        string audioFilePath,
        TimeSpan maxChunkDuration,
        string outputDirectory,
        CancellationToken cancellationToken = default)
    {
        var mediaInfo = FFProbe.Analyse(audioFilePath);
        var totalDuration = mediaInfo.Duration;
        var chunks = new List<AudioChunk>();
        int chunkIndex = 0;
        var currentOffset = TimeSpan.Zero;

        _logger.LogInformation(
            "Splitting {File} (duration: {Duration}) into chunks of max {MaxDuration}",
            audioFilePath, totalDuration, maxChunkDuration);

        while (currentOffset < totalDuration)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var chunkDuration = TimeSpan.FromTicks(
                Math.Min(maxChunkDuration.Ticks, (totalDuration - currentOffset).Ticks));

            string chunkFileName = $"chunk_{chunkIndex:D4}.wav";
            string chunkPath = Path.Combine(outputDirectory, chunkFileName);

            await FFMpegArguments
                .FromFileInput(audioFilePath, verifyExists: true, options => options
                    .Seek(currentOffset))
                .OutputToFile(chunkPath, overwrite: true, options => options
                    .WithDuration(chunkDuration)
                    .ForceFormat("wav"))
                .CancellableThrough(cancellationToken)
                .ProcessAsynchronously();

            chunks.Add(new AudioChunk
            {
                Index = chunkIndex,
                FilePath = chunkPath,
                Duration = chunkDuration,
                StartOffset = currentOffset
            });

            _logger.LogDebug(
                "Created chunk {Index} at offset {Offset}, duration {Duration}",
                chunkIndex, currentOffset, chunkDuration);

            currentOffset += chunkDuration;
            chunkIndex++;
        }

        return chunks;
    }

    private static void EnsureFFMpegInstalled()
    {
        var paths = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator);

        bool found = paths.Any(dir =>
            File.Exists(Path.Combine(dir, "ffmpeg")) ||
            File.Exists(Path.Combine(dir, "ffmpeg.exe")));

        if (!found)
        {
            throw new InvalidOperationException(
                "ffmpeg is required but was not found on PATH. Install it with: brew install ffmpeg");
        }
    }
}

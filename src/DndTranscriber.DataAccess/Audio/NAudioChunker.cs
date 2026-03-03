namespace DndTranscriber.DataAccess.Audio;

using DndTranscriber.Core.Interfaces;
using DndTranscriber.Core.Models;
using Microsoft.Extensions.Logging;
using NAudio.Wave;
using System;

public sealed class NAudioChunker : IAudioChunker
{
    private readonly ILogger<NAudioChunker> _logger;

    public NAudioChunker(ILogger<NAudioChunker> logger)
    {
        _logger = logger;
    }

    public bool NeedsChunking(string audioFilePath, TimeSpan maxChunkDuration)
    {
        using var reader = CreateReader(audioFilePath);
        return reader.TotalTime > maxChunkDuration;
    }

    public Task<IReadOnlyList<AudioChunk>> SplitAsync(
        string audioFilePath,
        TimeSpan maxChunkDuration,
        string outputDirectory,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(() => SplitInternal(
            audioFilePath, maxChunkDuration, outputDirectory, cancellationToken),
            cancellationToken);
    }

    private IReadOnlyList<AudioChunk> SplitInternal(
        string audioFilePath,
        TimeSpan maxChunkDuration,
        string outputDirectory,
        CancellationToken cancellationToken)
    {
        var chunks = new List<AudioChunk>();

        using var reader = CreateReader(audioFilePath);
        var totalDuration = reader.TotalTime;
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

            reader.CurrentTime = currentOffset;
            long bytesToRead = (long)(chunkDuration.TotalSeconds *
                reader.WaveFormat.AverageBytesPerSecond);

            using (var writer = new WaveFileWriter(chunkPath, reader.WaveFormat))
            {
                byte[] buffer = new byte[reader.WaveFormat.AverageBytesPerSecond];
                long totalBytesRead = 0;

                while (totalBytesRead < bytesToRead)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    int toRead = (int)Math.Min(buffer.Length, bytesToRead - totalBytesRead);
                    int bytesRead = reader.Read(buffer, 0, toRead);
                    if (bytesRead == 0) break;
                    writer.Write(buffer, 0, bytesRead);
                    totalBytesRead += bytesRead;
                }
            }

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

    private static WaveStream CreateReader(string filePath)
    {
        string ext = Path.GetExtension(filePath).ToLowerInvariant();
        return ext switch
        {
            ".mp3" => new Mp3FileReader(filePath),
            ".wav" => new WaveFileReader(filePath),
            // MediaFoundationReader supports mp4/m4a on Windows.
            // On macOS/Linux, pre-convert to wav using ffmpeg.
            ".mp4" or ".m4a" => new MediaFoundationReader(filePath),
            _ => throw new NotSupportedException(
                $"Audio format '{ext}' is not supported. Use .mp3, .wav, or .mp4.")
        };
    }
}

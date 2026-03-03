namespace DndTranscriber.Core.Services;

using DndTranscriber.Core.Interfaces;
using DndTranscriber.Core.Models;
using Microsoft.Extensions.Logging;

public sealed class TranscriptionOrchestrator
{
    private readonly ITranscriptionService _transcriptionService;
    private readonly ISummarizationService _summarizationService;
    private readonly IAudioChunker _audioChunker;
    private readonly ILogger<TranscriptionOrchestrator> _logger;

    public TranscriptionOrchestrator(
        ITranscriptionService transcriptionService,
        ISummarizationService summarizationService,
        IAudioChunker audioChunker,
        ILogger<TranscriptionOrchestrator> logger)
    {
        _transcriptionService = transcriptionService;
        _summarizationService = summarizationService;
        _audioChunker = audioChunker;
        _logger = logger;
    }

    public async Task<(string Transcription, SummarizationResult Summary)> ProcessAsync(
        string audioFilePath,
        TranscriptionOptions? transcriptionOptions = null,
        SummarizationOptions? summarizationOptions = null,
        CancellationToken cancellationToken = default)
    {
        transcriptionOptions ??= new TranscriptionOptions();
        summarizationOptions ??= new SummarizationOptions();

        _logger.LogInformation("Starting processing of {AudioFile}", audioFilePath);

        var chunkFilePaths = new List<string>();
        string tempDir = Path.Combine(Path.GetTempPath(), $"dndtranscriber_{Guid.NewGuid():N}");

        try
        {
            // Step 1: Chunk if needed
            if (_audioChunker.NeedsChunking(audioFilePath, transcriptionOptions.MaxChunkDuration))
            {
                _logger.LogInformation("Audio file requires chunking. Splitting...");
                Directory.CreateDirectory(tempDir);
                var chunks = await _audioChunker.SplitAsync(
                    audioFilePath,
                    transcriptionOptions.MaxChunkDuration,
                    tempDir,
                    cancellationToken);
                chunkFilePaths.AddRange(chunks.Select(c => c.FilePath));
                _logger.LogInformation("Split into {Count} chunks", chunks.Count);
            }
            else
            {
                chunkFilePaths.Add(audioFilePath);
            }

            // Step 2: Transcribe all chunks concurrently
            _logger.LogInformation("Transcribing {Count} chunk(s) concurrently...", chunkFilePaths.Count);

            var transcriptionTasks = chunkFilePaths
                .Select((path, i) =>
                {
                    _logger.LogInformation("Starting transcription for chunk {Current}/{Total}...",
                        i + 1, chunkFilePaths.Count);
                    return _transcriptionService.TranscribeAsync(path, transcriptionOptions, cancellationToken);
                })
                .ToArray();

            var results = await Task.WhenAll(transcriptionTasks);

            for (int i = 0; i < results.Length; i++)
            {
                if (!results[i].IsSuccess)
                {
                    throw new InvalidOperationException(
                        $"Transcription failed for chunk {i + 1}: {results[i].ErrorMessage}");
                }
            }

            // Step 3: Combine all transcription parts (in original chunk order)
            string fullTranscription = string.Join(Environment.NewLine, results.Select(r => r.Text));
            _logger.LogInformation(
                "Transcription complete. Total length: {Length} characters",
                fullTranscription.Length);

            // Step 4: Summarize
            _logger.LogInformation("Starting summarization...");
            var summaryResult = await _summarizationService.SummarizeAsync(
                fullTranscription,
                summarizationOptions,
                cancellationToken);

            if (!summaryResult.IsSuccess)
            {
                throw new InvalidOperationException(
                    $"Summarization failed: {summaryResult.ErrorMessage}");
            }

            _logger.LogInformation("Summarization complete. Tokens used: {Tokens}",
                summaryResult.TokensUsed);

            return (fullTranscription, summaryResult);
        }
        finally
        {
            // Clean up temporary chunk files
            if (Directory.Exists(tempDir))
            {
                try
                {
                    Directory.Delete(tempDir, recursive: true);
                    _logger.LogDebug("Cleaned up temp directory: {TempDir}", tempDir);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "Failed to clean up temp directory: {TempDir}", tempDir);
                }
            }
        }
    }
}

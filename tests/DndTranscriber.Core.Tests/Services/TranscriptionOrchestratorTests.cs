namespace DndTranscriber.Core.Tests.Services;

using DndTranscriber.Core.Interfaces;
using DndTranscriber.Core.Models;
using DndTranscriber.Core.Services;
using Microsoft.Extensions.Logging;
using Moq;

public class TranscriptionOrchestratorTests
{
    private readonly Mock<ITranscriptionService> _transcriptionService;
    private readonly Mock<ISummarizationService> _summarizationService;
    private readonly Mock<IAudioChunker> _audioChunker;
    private readonly Mock<ILogger<TranscriptionOrchestrator>> _logger;
    private readonly TranscriptionOrchestrator _orchestrator;

    public TranscriptionOrchestratorTests()
    {
        _transcriptionService = new Mock<ITranscriptionService>();
        _summarizationService = new Mock<ISummarizationService>();
        _audioChunker = new Mock<IAudioChunker>();
        _logger = new Mock<ILogger<TranscriptionOrchestrator>>();

        _orchestrator = new TranscriptionOrchestrator(
            _transcriptionService.Object,
            _summarizationService.Object,
            _audioChunker.Object,
            _logger.Object);
    }

    [Fact]
    public async Task ProcessAsync_NoChunkingNeeded_TranscribesAndSummarizes()
    {
        // Arrange
        string audioPath = "/test/audio.mp3";

        _audioChunker.Setup(c => c.NeedsChunking(audioPath, It.IsAny<TimeSpan>()))
            .Returns(false);

        _transcriptionService.Setup(t => t.TranscribeAsync(
                audioPath, It.IsAny<TranscriptionOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TranscriptionResult
            {
                Text = "The party entered the dungeon.",
                IsSuccess = true
            });

        _summarizationService.Setup(s => s.SummarizeAsync(
                It.IsAny<string>(), It.IsAny<SummarizationOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SummarizationResult
            {
                Summary = "- Party entered dungeon",
                IsSuccess = true,
                TokensUsed = 50
            });

        // Act
        var (transcription, summary) = await _orchestrator.ProcessAsync(audioPath);

        // Assert
        Assert.Equal("The party entered the dungeon.", transcription);
        Assert.Equal("- Party entered dungeon", summary.Summary);
        Assert.True(summary.IsSuccess);

        _audioChunker.Verify(c => c.SplitAsync(
            It.IsAny<string>(), It.IsAny<TimeSpan>(),
            It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ProcessAsync_ChunkingNeeded_SplitsAndTranscribesAllChunks()
    {
        // Arrange
        string audioPath = "/test/long_audio.mp3";

        _audioChunker.Setup(c => c.NeedsChunking(audioPath, It.IsAny<TimeSpan>()))
            .Returns(true);

        _audioChunker.Setup(c => c.SplitAsync(
                audioPath, It.IsAny<TimeSpan>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AudioChunk>
            {
                new() { Index = 0, FilePath = "/tmp/chunk_0000.wav", Duration = TimeSpan.FromMinutes(60) },
                new() { Index = 1, FilePath = "/tmp/chunk_0001.wav", Duration = TimeSpan.FromMinutes(30) }
            });

        _transcriptionService.Setup(t => t.TranscribeAsync(
                "/tmp/chunk_0000.wav", It.IsAny<TranscriptionOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TranscriptionResult { Text = "Part one.", IsSuccess = true });

        _transcriptionService.Setup(t => t.TranscribeAsync(
                "/tmp/chunk_0001.wav", It.IsAny<TranscriptionOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TranscriptionResult { Text = "Part two.", IsSuccess = true });

        _summarizationService.Setup(s => s.SummarizeAsync(
                It.IsAny<string>(), It.IsAny<SummarizationOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SummarizationResult
            {
                Summary = "- Combined summary",
                IsSuccess = true,
                TokensUsed = 100
            });

        // Act
        var (transcription, summary) = await _orchestrator.ProcessAsync(audioPath);

        // Assert
        Assert.Contains("Part one.", transcription);
        Assert.Contains("Part two.", transcription);
        Assert.Equal("- Combined summary", summary.Summary);

        _transcriptionService.Verify(t => t.TranscribeAsync(
            It.IsAny<string>(), It.IsAny<TranscriptionOptions>(),
            It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task ProcessAsync_TranscriptionFails_ThrowsWithMessage()
    {
        // Arrange
        string audioPath = "/test/audio.mp3";

        _audioChunker.Setup(c => c.NeedsChunking(audioPath, It.IsAny<TimeSpan>()))
            .Returns(false);

        _transcriptionService.Setup(t => t.TranscribeAsync(
                audioPath, It.IsAny<TranscriptionOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TranscriptionResult
            {
                Text = string.Empty,
                IsSuccess = false,
                ErrorMessage = "API key invalid"
            });

        // Act & Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _orchestrator.ProcessAsync(audioPath));

        Assert.Contains("API key invalid", ex.Message);

        _summarizationService.Verify(s => s.SummarizeAsync(
            It.IsAny<string>(), It.IsAny<SummarizationOptions>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ProcessAsync_SummarizationFails_ThrowsWithMessage()
    {
        // Arrange
        string audioPath = "/test/audio.mp3";

        _audioChunker.Setup(c => c.NeedsChunking(audioPath, It.IsAny<TimeSpan>()))
            .Returns(false);

        _transcriptionService.Setup(t => t.TranscribeAsync(
                audioPath, It.IsAny<TranscriptionOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TranscriptionResult
            {
                Text = "Some transcription text",
                IsSuccess = true
            });

        _summarizationService.Setup(s => s.SummarizeAsync(
                It.IsAny<string>(), It.IsAny<SummarizationOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SummarizationResult
            {
                Summary = string.Empty,
                IsSuccess = false,
                ErrorMessage = "Rate limit exceeded"
            });

        // Act & Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _orchestrator.ProcessAsync(audioPath));

        Assert.Contains("Rate limit exceeded", ex.Message);
    }

    [Fact]
    public async Task ProcessAsync_CombinesChunksInOrder()
    {
        // Arrange
        string audioPath = "/test/audio.mp3";

        _audioChunker.Setup(c => c.NeedsChunking(audioPath, It.IsAny<TimeSpan>()))
            .Returns(true);

        _audioChunker.Setup(c => c.SplitAsync(
                audioPath, It.IsAny<TimeSpan>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AudioChunk>
            {
                new() { Index = 0, FilePath = "/tmp/chunk_0.wav", Duration = TimeSpan.FromMinutes(60) },
                new() { Index = 1, FilePath = "/tmp/chunk_1.wav", Duration = TimeSpan.FromMinutes(60) },
                new() { Index = 2, FilePath = "/tmp/chunk_2.wav", Duration = TimeSpan.FromMinutes(30) }
            });

        _transcriptionService.Setup(t => t.TranscribeAsync(
                "/tmp/chunk_0.wav", It.IsAny<TranscriptionOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TranscriptionResult { Text = "First", IsSuccess = true });

        _transcriptionService.Setup(t => t.TranscribeAsync(
                "/tmp/chunk_1.wav", It.IsAny<TranscriptionOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TranscriptionResult { Text = "Second", IsSuccess = true });

        _transcriptionService.Setup(t => t.TranscribeAsync(
                "/tmp/chunk_2.wav", It.IsAny<TranscriptionOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TranscriptionResult { Text = "Third", IsSuccess = true });

        string capturedText = "";
        _summarizationService.Setup(s => s.SummarizeAsync(
                It.IsAny<string>(), It.IsAny<SummarizationOptions>(), It.IsAny<CancellationToken>()))
            .Callback<string, SummarizationOptions, CancellationToken>((text, _, _) => capturedText = text)
            .ReturnsAsync(new SummarizationResult
            {
                Summary = "summary",
                IsSuccess = true
            });

        // Act
        await _orchestrator.ProcessAsync(audioPath);

        // Assert — text is combined in chunk order
        var lines = capturedText.Split(Environment.NewLine);
        Assert.Equal("First", lines[0]);
        Assert.Equal("Second", lines[1]);
        Assert.Equal("Third", lines[2]);
    }

    [Fact]
    public async Task ProcessAsync_PassesOptionsToServices()
    {
        // Arrange
        string audioPath = "/test/audio.mp3";
        var transcriptionOptions = new TranscriptionOptions { Locale = "fr-FR" };
        var summarizationOptions = new SummarizationOptions { MaxResponseTokens = 500 };

        _audioChunker.Setup(c => c.NeedsChunking(audioPath, It.IsAny<TimeSpan>()))
            .Returns(false);

        _transcriptionService.Setup(t => t.TranscribeAsync(
                audioPath, It.IsAny<TranscriptionOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TranscriptionResult { Text = "Texte", IsSuccess = true });

        _summarizationService.Setup(s => s.SummarizeAsync(
                It.IsAny<string>(), It.IsAny<SummarizationOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SummarizationResult { Summary = "Résumé", IsSuccess = true });

        // Act
        await _orchestrator.ProcessAsync(audioPath, transcriptionOptions, summarizationOptions);

        // Assert
        _transcriptionService.Verify(t => t.TranscribeAsync(
            audioPath,
            It.Is<TranscriptionOptions>(o => o.Locale == "fr-FR"),
            It.IsAny<CancellationToken>()), Times.Once);

        _summarizationService.Verify(s => s.SummarizeAsync(
            It.IsAny<string>(),
            It.Is<SummarizationOptions>(o => o.MaxResponseTokens == 500),
            It.IsAny<CancellationToken>()), Times.Once);
    }
}

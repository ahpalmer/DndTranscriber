namespace DndTranscriber.DataAccess.Summarization;

using Anthropic;
using Anthropic.Models.Messages;
using DndTranscriber.Core.Interfaces;
using DndTranscriber.Core.Models;
using DndTranscriber.DataAccess.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

public sealed class AnthropicSummarizationService : ISummarizationService
{
    private readonly AnthropicSettings _settings;
    private readonly ILogger<AnthropicSummarizationService> _logger;

    public AnthropicSummarizationService(
        IOptions<AnthropicSettings> settings,
        ILogger<AnthropicSummarizationService> logger)
    {
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<SummarizationResult> SummarizeAsync(
        string fullText,
        SummarizationOptions options,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var client = new AnthropicClient() { ApiKey = _settings.AnthropicApiKey };

            var parameters = new MessageCreateParams
            {
                Model = _settings.Model,
                MaxTokens = options.MaxResponseTokens,
                Temperature = 0.3,
                System = options.SystemPrompt,
                Messages = new[]
                {
                    new MessageParam
                    {
                        Role = Role.User,
                        Content =
                            $"Here is the transcription of a D&D session. " +
                            $"Please summarize it:\n\n{fullText}"
                    }
                }
            };

            var message = await client.Messages.Create(parameters, cancellationToken);

            string summaryText = string.Empty;
            if (message.Content[0].TryPickText(out var textBlock))
            {
                summaryText = textBlock.Text;
            }

            int tokensUsed = (int)(message.Usage.InputTokens + message.Usage.OutputTokens);

            _logger.LogInformation(
                "Summarization complete. Tokens: {Tokens}", tokensUsed);

            return new SummarizationResult
            {
                Summary = summaryText,
                IsSuccess = true,
                TokensUsed = tokensUsed
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Summarization failed");
            return new SummarizationResult
            {
                Summary = string.Empty,
                IsSuccess = false,
                ErrorMessage = ex.Message
            };
        }
    }
}

namespace DndTranscriber.DataAccess.Transcription;

using System.Net.Http.Json;
using DndTranscriber.Core.Interfaces;
using DndTranscriber.Core.Models;
using DndTranscriber.DataAccess.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

public sealed class AzureBatchTranscriptionService : ITranscriptionService
{
    private readonly HttpClient _httpClient;
    private readonly AzureSpeechSettings _settings;
    private readonly ILogger<AzureBatchTranscriptionService> _logger;

    public AzureBatchTranscriptionService(
        HttpClient httpClient,
        IOptions<AzureSpeechSettings> settings,
        ILogger<AzureBatchTranscriptionService> logger)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<TranscriptionResult> TranscribeAsync(
        string audioFilePath,
        TranscriptionOptions options,
        CancellationToken cancellationToken = default)
    {
        // Azure Batch Transcription requires audio to be accessible via URL
        // (Azure Blob Storage SAS URL). The audioFilePath parameter should be
        // a SAS URL pointing to the uploaded audio file.
        //
        // Workflow:
        // 1. POST to create transcription job
        // 2. Poll GET until status is "Succeeded" or "Failed"
        // 3. GET files list, find Transcription entries
        // 4. GET each result file, extract display text

        string baseUrl = $"https://{_settings.Region}.api.cognitive.microsoft.com";

        try
        {
            // Step 1: Submit transcription job
            var request = new BatchTranscriptionRequest
            {
                ContentUrls = [audioFilePath],
                Locale = options.Locale,
                DisplayName = $"DndTranscriber-{DateTime.UtcNow:yyyyMMdd-HHmmss}",
                Properties = new BatchTranscriptionProperties
                {
                    TimeToLiveHours = _settings.TimeToLiveHours,
                    DiarizationEnabled = options.EnableDiarization,
                    WordLevelTimestampsEnabled = false
                }
            };

            var submitUrl = $"{baseUrl}/speechtotext/transcriptions:submit?api-version=2024-11-15";

            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, submitUrl);
            httpRequest.Headers.Add("Ocp-Apim-Subscription-Key", _settings.SpeechApiKey);
            httpRequest.Content = JsonContent.Create(request);

            var submitResponse = await _httpClient.SendAsync(httpRequest, cancellationToken);
            submitResponse.EnsureSuccessStatusCode();

            var transcription = await submitResponse.Content
                .ReadFromJsonAsync<BatchTranscriptionResponse>(cancellationToken: cancellationToken);

            string transcriptionUrl = transcription!.Self;
            _logger.LogInformation("Transcription job created: {Url}", transcriptionUrl);

            // Step 2: Poll for completion
            var maxWait = TimeSpan.FromMinutes(_settings.MaxWaitTimeMinutes);
            var pollInterval = TimeSpan.FromSeconds(_settings.PollingIntervalSeconds);
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            while (stopwatch.Elapsed < maxWait)
            {
                await Task.Delay(pollInterval, cancellationToken);

                using var statusRequest = new HttpRequestMessage(HttpMethod.Get, transcriptionUrl);
                statusRequest.Headers.Add("Ocp-Apim-Subscription-Key", _settings.SpeechApiKey);

                var statusResponse = await _httpClient.SendAsync(statusRequest, cancellationToken);
                statusResponse.EnsureSuccessStatusCode();

                var status = await statusResponse.Content
                    .ReadFromJsonAsync<BatchTranscriptionResponse>(cancellationToken: cancellationToken);

                _logger.LogDebug("Transcription status: {Status}", status!.Status);

                if (status.Status == "Succeeded")
                {
                    return await GetTranscriptionResultAsync(
                        status.Links!.Files!, cancellationToken);
                }

                if (status.Status == "Failed")
                {
                    return new TranscriptionResult
                    {
                        Text = string.Empty,
                        IsSuccess = false,
                        ErrorMessage = "Azure batch transcription job failed."
                    };
                }
            }

            return new TranscriptionResult
            {
                Text = string.Empty,
                IsSuccess = false,
                ErrorMessage = $"Transcription timed out after {maxWait.TotalMinutes} minutes."
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Transcription request failed");
            return new TranscriptionResult
            {
                Text = string.Empty,
                IsSuccess = false,
                ErrorMessage = ex.Message
            };
        }
    }

    private async Task<TranscriptionResult> GetTranscriptionResultAsync(
        string filesUrl, CancellationToken cancellationToken)
    {
        using var filesRequest = new HttpRequestMessage(HttpMethod.Get, filesUrl);
        filesRequest.Headers.Add("Ocp-Apim-Subscription-Key", _settings.SpeechApiKey);

        var filesResponse = await _httpClient.SendAsync(filesRequest, cancellationToken);
        filesResponse.EnsureSuccessStatusCode();

        var files = await filesResponse.Content
            .ReadFromJsonAsync<TranscriptionFilesResponse>(cancellationToken: cancellationToken);

        var transcriptionEntries = files!.Values
            .Where(f => f.Kind == "Transcription")
            .ToList();

        var allText = new List<string>();

        foreach (var entry in transcriptionEntries)
        {
            var resultResponse = await _httpClient.GetAsync(
                entry.Links.ContentUrl, cancellationToken);
            resultResponse.EnsureSuccessStatusCode();

            var result = await resultResponse.Content
                .ReadFromJsonAsync<TranscriptionResultFile>(cancellationToken: cancellationToken);

            if (result?.CombinedRecognizedPhrases != null)
            {
                foreach (var phrase in result.CombinedRecognizedPhrases)
                {
                    allText.Add(phrase.Display);
                }
            }
        }

        return new TranscriptionResult
        {
            Text = string.Join(Environment.NewLine, allText),
            IsSuccess = true
        };
    }
}

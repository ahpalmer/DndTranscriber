namespace DndTranscriber.DataAccess.Transcription;

using System.Net.Http.Json;
using Azure.Storage.Blobs;
using Azure.Storage.Sas;
using DndTranscriber.Core.Interfaces;
using DndTranscriber.Core.Models;
using DndTranscriber.DataAccess.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

public sealed class AzureBatchTranscriptionService : ITranscriptionService
{
    private readonly HttpClient _httpClient;
    private readonly AzureSpeechSettings _speechSettings;
    private readonly AzureBlobSettings _blobSettings;
    private readonly ILogger<AzureBatchTranscriptionService> _logger;

    public AzureBatchTranscriptionService(
        HttpClient httpClient,
        IOptions<AzureSpeechSettings> speechSettings,
        IOptions<AzureBlobSettings> blobSettings,
        ILogger<AzureBatchTranscriptionService> logger)
    {
        _httpClient = httpClient;
        _speechSettings = speechSettings.Value;
        _blobSettings = blobSettings.Value;
        _logger = logger;
    }

    public async Task<TranscriptionResult> TranscribeAsync(
        string audioFilePath,
        TranscriptionOptions options,
        CancellationToken cancellationToken = default)
    {
        string? blobName = null;

        try
        {
            // Upload local file to blob storage and get SAS URL
            _logger.LogInformation("Uploading {File} to Azure Blob Storage...", audioFilePath);
            var (sasUrl, uploadedBlobName) = await UploadAndGetSasUrlAsync(audioFilePath, cancellationToken);
            blobName = uploadedBlobName;
            _logger.LogInformation("Upload complete. Blob: {Blob}", blobName);

            // Submit batch transcription with the SAS URL
            return await RunBatchTranscriptionAsync(sasUrl, options, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Batch transcription failed");
            return new TranscriptionResult
            {
                Text = string.Empty,
                IsSuccess = false,
                ErrorMessage = ex.Message
            };
        }
        finally
        {
            // Clean up the uploaded blob
            if (blobName != null)
            {
                await TryDeleteBlobAsync(blobName);
            }
        }
    }

    private async Task<(string SasUrl, string BlobName)> UploadAndGetSasUrlAsync(
        string localFilePath, CancellationToken cancellationToken)
    {
        var blobServiceClient = new BlobServiceClient(_blobSettings.ConnectionString);
        var containerClient = blobServiceClient.GetBlobContainerClient(_blobSettings.ContainerName);

        _logger.LogDebug("Ensuring blob container '{Container}' exists...", _blobSettings.ContainerName);
        await containerClient.CreateIfNotExistsAsync(cancellationToken: cancellationToken);

        string blobName = $"{Guid.NewGuid():N}{Path.GetExtension(localFilePath)}";
        var blobClient = containerClient.GetBlobClient(blobName);

        _logger.LogInformation("Uploading to blob: {Blob}, file size: {Size} bytes",
            blobName, new FileInfo(localFilePath).Length);

        await using var stream = File.OpenRead(localFilePath);
        await blobClient.UploadAsync(stream, overwrite: true, cancellationToken);

        // Generate SAS URL
        var sasBuilder = new BlobSasBuilder
        {
            BlobContainerName = _blobSettings.ContainerName,
            BlobName = blobName,
            Resource = "b",
            ExpiresOn = DateTimeOffset.UtcNow.AddHours(_blobSettings.SasTokenExpiryHours)
        };
        sasBuilder.SetPermissions(BlobSasPermissions.Read);

        var sasUri = blobClient.GenerateSasUri(sasBuilder);
        _logger.LogDebug("Generated SAS URL (expires in {Hours}h)", _blobSettings.SasTokenExpiryHours);

        return (sasUri.ToString(), blobName);
    }

    private async Task<TranscriptionResult> RunBatchTranscriptionAsync(
        string sasUrl, TranscriptionOptions options, CancellationToken cancellationToken)
    {
        string baseUrl = $"https://{_speechSettings.Region}.api.cognitive.microsoft.com";

        // Step 1: Submit transcription job
        var request = new BatchTranscriptionRequest
        {
            ContentUrls = [sasUrl],
            Locale = options.Locale,
            DisplayName = $"DndTranscriber-{DateTime.UtcNow:yyyyMMdd-HHmmss}",
            Properties = new BatchTranscriptionProperties
            {
                TimeToLiveHours = _speechSettings.TimeToLiveHours,
                DiarizationEnabled = options.EnableDiarization,
                WordLevelTimestampsEnabled = false
            }
        };

        var submitUrl = $"{baseUrl}/speechtotext/transcriptions:submit?api-version=2024-11-15";

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, submitUrl);
        httpRequest.Headers.Add("Ocp-Apim-Subscription-Key", _speechSettings.SpeechApiKey);
        httpRequest.Content = JsonContent.Create(request);

        _logger.LogInformation("Submitting batch transcription job...");
        var submitResponse = await _httpClient.SendAsync(httpRequest, cancellationToken);
        submitResponse.EnsureSuccessStatusCode();

        var transcription = await submitResponse.Content
            .ReadFromJsonAsync<BatchTranscriptionResponse>(cancellationToken: cancellationToken);

        string transcriptionUrl = transcription!.Self;
        _logger.LogInformation("Transcription job created: {Url}", transcriptionUrl);

        // Step 2: Poll for completion
        var maxWait = TimeSpan.FromMinutes(_speechSettings.MaxWaitTimeMinutes);
        var pollInterval = TimeSpan.FromSeconds(_speechSettings.PollingIntervalSeconds);
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        while (stopwatch.Elapsed < maxWait)
        {
            await Task.Delay(pollInterval, cancellationToken);

            using var statusRequest = new HttpRequestMessage(HttpMethod.Get, transcriptionUrl);
            statusRequest.Headers.Add("Ocp-Apim-Subscription-Key", _speechSettings.SpeechApiKey);

            var statusResponse = await _httpClient.SendAsync(statusRequest, cancellationToken);
            statusResponse.EnsureSuccessStatusCode();

            var status = await statusResponse.Content
                .ReadFromJsonAsync<BatchTranscriptionResponse>(cancellationToken: cancellationToken);

            _logger.LogInformation("Transcription status: {Status} (elapsed: {Elapsed})",
                status!.Status, stopwatch.Elapsed);

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

    private async Task<TranscriptionResult> GetTranscriptionResultAsync(
        string filesUrl, CancellationToken cancellationToken)
    {
        using var filesRequest = new HttpRequestMessage(HttpMethod.Get, filesUrl);
        filesRequest.Headers.Add("Ocp-Apim-Subscription-Key", _speechSettings.SpeechApiKey);

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

        _logger.LogInformation("Batch transcription complete. Retrieved {Count} text segments.", allText.Count);

        return new TranscriptionResult
        {
            Text = string.Join(Environment.NewLine, allText),
            IsSuccess = true
        };
    }

    private async Task TryDeleteBlobAsync(string blobName)
    {
        try
        {
            var blobServiceClient = new BlobServiceClient(_blobSettings.ConnectionString);
            var containerClient = blobServiceClient.GetBlobContainerClient(_blobSettings.ContainerName);
            var blobClient = containerClient.GetBlobClient(blobName);
            await blobClient.DeleteIfExistsAsync();
            _logger.LogDebug("Cleaned up blob: {Blob}", blobName);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to clean up blob: {Blob}", blobName);
        }
    }
}

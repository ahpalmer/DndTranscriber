namespace DndTranscriber.DataAccess.Transcription;

using System.Text;
using DndTranscriber.Core.Interfaces;
using DndTranscriber.Core.Models;
using DndTranscriber.DataAccess.Configuration;
using FFMpegCore;
using Microsoft.CognitiveServices.Speech;
using Microsoft.CognitiveServices.Speech.Audio;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

public sealed class AzureSpeechSdkTranscriptionService : ITranscriptionService
{
    private readonly AzureSpeechSettings _settings;
    private readonly ILogger<AzureSpeechSdkTranscriptionService> _logger;

    public AzureSpeechSdkTranscriptionService(
        IOptions<AzureSpeechSettings> settings,
        ILogger<AzureSpeechSdkTranscriptionService> logger)
    {
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<TranscriptionResult> TranscribeAsync(
        string audioFilePath,
        TranscriptionOptions options,
        CancellationToken cancellationToken = default)
    {
        string? tempWavFile = null;

        try
        {
            string wavPath = audioFilePath;

            if (!audioFilePath.EndsWith(".wav", StringComparison.OrdinalIgnoreCase))
            {
                tempWavFile = Path.Combine(
                    Path.GetTempPath(),
                    $"dndtranscriber_{Guid.NewGuid():N}.wav");

                _logger.LogInformation(
                    "Converting {Input} to WAV format at {Output}",
                    audioFilePath, tempWavFile);

                await FFMpegArguments
                    .FromFileInput(audioFilePath)
                    .OutputToFile(tempWavFile, overwrite: true, options => options
                        .WithAudioSamplingRate(16000)
                        .WithCustomArgument("-ac 1")
                        .WithCustomArgument("-acodec pcm_s16le"))
                    .ProcessAsynchronously();

                wavPath = tempWavFile;
            }

            var speechConfig = SpeechConfig.FromSubscription(_settings.SpeechApiKey, _settings.Region);
            speechConfig.SpeechRecognitionLanguage = options.Locale;

            using var audioConfig = AudioConfig.FromWavFileInput(wavPath);
            using var recognizer = new SpeechRecognizer(speechConfig, audioConfig);

            var results = new StringBuilder();
            var stopSignal = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            recognizer.Recognized += (_, e) =>
            {
                if (e.Result.Reason == ResultReason.RecognizedSpeech && !string.IsNullOrWhiteSpace(e.Result.Text))
                {
                    if (results.Length > 0)
                        results.Append(' ');
                    results.Append(e.Result.Text);
                }
            };

            recognizer.Canceled += (_, e) =>
            {
                if (e.Reason == CancellationReason.Error)
                {
                    _logger.LogError(
                        "Speech recognition canceled. Error code: {Code}, Details: {Details}",
                        e.ErrorCode, e.ErrorDetails);
                    stopSignal.TrySetException(new InvalidOperationException(
                        $"Speech recognition error: {e.ErrorCode} — {e.ErrorDetails}"));
                }
                else
                {
                    _logger.LogInformation("Recognition canceled (end of stream).");
                    stopSignal.TrySetResult(true);
                }
            };

            recognizer.SessionStopped += (_, _) =>
            {
                _logger.LogInformation("Speech recognition session completed.");
                stopSignal.TrySetResult(true);
            };

            _logger.LogInformation("Starting continuous recognition...");
            await recognizer.StartContinuousRecognitionAsync();

            using var registration = cancellationToken.Register(() =>
                stopSignal.TrySetCanceled(cancellationToken));

            await stopSignal.Task;

            string text = results.ToString();
            _logger.LogInformation(
                "Transcription complete. Length: {Length} characters", text.Length);

            return new TranscriptionResult
            {
                Text = text,
                IsSuccess = true
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Speech SDK transcription failed");
            return new TranscriptionResult
            {
                Text = string.Empty,
                IsSuccess = false,
                ErrorMessage = ex.Message
            };
        }
        finally
        {
            if (tempWavFile != null && File.Exists(tempWavFile))
            {
                try
                {
                    File.Delete(tempWavFile);
                    _logger.LogDebug("Cleaned up temp WAV file: {Path}", tempWavFile);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to clean up temp WAV file: {Path}", tempWavFile);
                }
            }
        }
    }
}

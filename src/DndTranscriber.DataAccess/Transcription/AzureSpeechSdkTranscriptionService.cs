namespace DndTranscriber.DataAccess.Transcription;

using System.Text;
using DndTranscriber.Core.Interfaces;
using DndTranscriber.Core.Models;
using DndTranscriber.DataAccess.Configuration;
using Microsoft.CognitiveServices.Speech;
using Microsoft.CognitiveServices.Speech.Audio;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NAudio.Wave;

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

                ConvertToWav(audioFilePath, tempWavFile);

                _logger.LogInformation("WAV conversion complete: {Output}", tempWavFile);
                wavPath = tempWavFile;
            }

            // Validate WAV file before sending to Azure
            ValidateWavFile(wavPath);

            _logger.LogInformation(
                "Configuring Azure Speech SDK with region: {Region}, locale: {Locale}",
                _settings.Region, options.Locale);

            var speechConfig = SpeechConfig.FromSubscription(_settings.SpeechApiKey, _settings.Region);
            speechConfig.SpeechRecognitionLanguage = options.Locale;
            speechConfig.SetProperty(PropertyId.Speech_LogFilename, Path.Combine(Path.GetTempPath(), "azure_speech_sdk.log"));

            _logger.LogInformation("Creating audio config from WAV file: {Path}", wavPath);
            using var audioConfig = AudioConfig.FromWavFileInput(wavPath);

            _logger.LogInformation("Creating speech recognizer...");
            using var recognizer = new SpeechRecognizer(speechConfig, audioConfig);

            var results = new StringBuilder();
            int recognizedCount = 0;
            var stopSignal = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            recognizer.Recognized += (_, e) =>
            {
                recognizedCount++;

                if (e.Result.Reason == ResultReason.RecognizedSpeech && !string.IsNullOrWhiteSpace(e.Result.Text))
                {
                    if (results.Length > 0)
                        results.Append(' ');
                    results.Append(e.Result.Text);
                }
                else if (e.Result.Reason == ResultReason.NoMatch)
                {
                    _logger.LogWarning("No speech could be recognized in segment #{Count}.", recognizedCount);
                }
            };

            recognizer.Canceled += (_, e) =>
            {
                _logger.LogInformation(
                    "Recognition canceled. Reason: {Reason}, ErrorCode: {ErrorCode}",
                    e.Reason, e.ErrorCode);

                if (e.Reason == CancellationReason.Error)
                {
                    _logger.LogError(
                        "Speech recognition error. ErrorCode: {Code}, Details: {Details}",
                        e.ErrorCode, e.ErrorDetails);
                    stopSignal.TrySetException(new InvalidOperationException(
                        $"Speech recognition error: {e.ErrorCode} — {e.ErrorDetails}"));
                }
                else
                {
                    _logger.LogInformation("Recognition canceled normally (end of stream). Recognized {Count} segments total.", recognizedCount);
                    stopSignal.TrySetResult(true);
                }
            };

            recognizer.SessionStarted += (_, _) =>
            {
                _logger.LogInformation("Speech recognition session started.");
            };

            recognizer.SessionStopped += (_, _) =>
            {
                _logger.LogInformation("Speech recognition session stopped. Recognized {Count} segments total.", recognizedCount);
                stopSignal.TrySetResult(true);
            };

            recognizer.SpeechStartDetected += (_, e) =>
            {
                _logger.LogInformation("Speech start detected at offset: {Offset}", e.Offset);
            };

            recognizer.SpeechEndDetected += (_, e) =>
            {
                _logger.LogInformation("Speech end detected at offset: {Offset}", e.Offset);
            };

            _logger.LogInformation("Starting continuous recognition...");
            await recognizer.StartContinuousRecognitionAsync();
            _logger.LogInformation("Continuous recognition started successfully. Waiting for results...");

            using var registration = cancellationToken.Register(() =>
            {
                _logger.LogWarning("Cancellation requested, stopping recognition...");
                stopSignal.TrySetCanceled(cancellationToken);
            });

            // Wait with a timeout to prevent indefinite hangs
            var timeoutTask = Task.Delay(TimeSpan.FromMinutes(30), cancellationToken);
            var completedTask = await Task.WhenAny(stopSignal.Task, timeoutTask);

            if (completedTask == timeoutTask)
            {
                _logger.LogError("Recognition timed out after 30 minutes. Recognized {Count} segments before timeout.", recognizedCount);
                throw new TimeoutException("Speech recognition timed out after 30 minutes.");
            }

            await stopSignal.Task; // Propagate any exception

            _logger.LogInformation("Stopping continuous recognition...");
            await recognizer.StopContinuousRecognitionAsync();
            _logger.LogInformation("Continuous recognition stopped.");

            string text = results.ToString();
            _logger.LogInformation(
                "Transcription complete. Length: {Length} characters, Recognized segments: {Count}",
                text.Length, recognizedCount);

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

    private void ConvertToWav(string inputPath, string outputPath)
    {
        _logger.LogInformation("Starting NAudio WAV conversion: {Input} -> {Output}", inputPath, outputPath);

        string ext = Path.GetExtension(inputPath).ToLowerInvariant();
        _logger.LogDebug("Input file extension: {Extension}", ext);

        using WaveStream reader = ext switch
        {
            ".mp3" => new Mp3FileReader(inputPath),
            ".mp4" or ".m4a" or ".wma" or ".aac" => new MediaFoundationReader(inputPath),
            _ => throw new NotSupportedException(
                $"Audio format '{ext}' is not supported for conversion. Use .mp3, .wav, .mp4, .m4a, .wma, or .aac.")
        };

        _logger.LogInformation(
            "Input audio: SampleRate={SampleRate}, Channels={Channels}, BitsPerSample={Bits}, Duration={Duration}",
            reader.WaveFormat.SampleRate, reader.WaveFormat.Channels,
            reader.WaveFormat.BitsPerSample, reader.TotalTime);

        // Azure Speech SDK requires 16kHz, mono, 16-bit PCM
        var targetFormat = new WaveFormat(16000, 16, 1);

        _logger.LogDebug("Target format: 16kHz, mono, 16-bit PCM");

        using var resampler = new MediaFoundationResampler(reader, targetFormat);
        resampler.ResamplerQuality = 60;

        WaveFileWriter.CreateWaveFile(outputPath, resampler);

        var fileInfo = new FileInfo(outputPath);
        _logger.LogInformation("WAV conversion complete. Output file size: {Size} bytes", fileInfo.Length);
    }

    private void ValidateWavFile(string wavPath)
    {
        var fileInfo = new FileInfo(wavPath);
        _logger.LogInformation("Validating WAV file: {Path}, Size: {Size} bytes", wavPath, fileInfo.Length);

        if (fileInfo.Length == 0)
        {
            throw new InvalidOperationException($"WAV file is empty: {wavPath}");
        }

        using var reader = new WaveFileReader(wavPath);
        _logger.LogInformation(
            "WAV file details: SampleRate={SampleRate}, Channels={Channels}, BitsPerSample={Bits}, Encoding={Encoding}, Duration={Duration}",
            reader.WaveFormat.SampleRate, reader.WaveFormat.Channels,
            reader.WaveFormat.BitsPerSample, reader.WaveFormat.Encoding, reader.TotalTime);

        if (reader.TotalTime.TotalSeconds < 0.1)
        {
            _logger.LogWarning("WAV file is very short ({Duration}). This may cause recognition issues.", reader.TotalTime);
        }
    }
}

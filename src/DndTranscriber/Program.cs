using DndTranscriber.Core.Interfaces;
using DndTranscriber.Core.Models;
using DndTranscriber.Core.Services;
using DndTranscriber.DataAccess.Audio;
using DndTranscriber.DataAccess.Configuration;
using DndTranscriber.DataAccess.Summarization;
using DndTranscriber.DataAccess.Transcription;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Runtime.InteropServices;

if (args.Length < 1)
{
    Console.WriteLine("Usage: DndTranscriber <audio-file> [output-file]");
    Console.WriteLine();
    Console.WriteLine("Arguments:");
    Console.WriteLine("  audio-file          Path to local audio file (MP3, WAV, etc.)");
    Console.WriteLine("  output-file         (Optional) Path to save the summary. If omitted, prints to console.");
    return 1;
}

string audioInput = args[0];
string? outputFile = args.Length > 1 ? args[1] : null;

// Build configuration
var configuration = new ConfigurationBuilder()
    .AddJsonFile("appsettings.json", optional: false)
    .AddJsonFile("appsettings.Development.json", optional: true)
    .AddEnvironmentVariables("DNDTRANSCRIBER_")
    .AddUserSecrets<Program>(optional: true)
    .Build();

// Build DI container
var services = new ServiceCollection();

services.Configure<AzureSpeechSettings>(
    configuration.GetSection(AzureSpeechSettings.SectionName));
services.Configure<AnthropicSettings>(
    configuration.GetSection(AnthropicSettings.SectionName));

services.AddLogging(builder =>
{
    builder.AddConsole();
    builder.SetMinimumLevel(LogLevel.Information);
});

services.AddSingleton<ITranscriptionService, AzureSpeechSdkTranscriptionService>();
if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
    services.AddSingleton<IAudioChunker, FFMpegAudioChunker>();
else
    services.AddSingleton<IAudioChunker, NAudioChunker>();
services.AddSingleton<ISummarizationService, AnthropicSummarizationService>();
services.AddTransient<TranscriptionOrchestrator>();

var serviceProvider = services.BuildServiceProvider();

// Run the pipeline
var orchestrator = serviceProvider.GetRequiredService<TranscriptionOrchestrator>();

try
{
    Console.WriteLine($"Processing: {audioInput}");
    Console.WriteLine("This may take several minutes for long recordings...");
    Console.WriteLine();

    var (transcription, summary) = await orchestrator.ProcessAsync(
        audioInput,
        cancellationToken: CancellationToken.None);

    Console.WriteLine("=== SUMMARY ===");
    Console.WriteLine();
    Console.WriteLine(summary.Summary);

    if (outputFile != null)
    {
        await File.WriteAllTextAsync(outputFile, summary.Summary);
        Console.WriteLine();
        Console.WriteLine($"Summary saved to: {outputFile}");
    }

    string transcriptionOutputFile = outputFile != null
        ? Path.ChangeExtension(outputFile, ".transcription.txt")
        : "transcription.txt";
    await File.WriteAllTextAsync(transcriptionOutputFile, transcription);
    Console.WriteLine($"Full transcription saved to: {transcriptionOutputFile}");

    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Error: {ex.Message}");
    return 1;
}

using DndTranscriber.Core.Interfaces;
using DndTranscriber.Core.Services;
using DndTranscriber.DataAccess.Audio;
using DndTranscriber.DataAccess.Configuration;
using DndTranscriber.DataAccess.Summarization;
using DndTranscriber.DataAccess.Transcription;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

// Parse flags
bool useBatch = args.Contains("--batch", StringComparer.OrdinalIgnoreCase);
var positionalArgs = args.Where(a => !a.StartsWith("--")).ToArray();

if (positionalArgs.Length < 1)
{
    Console.WriteLine("Usage: DndTranscriber <audio-file> [output-file] [--batch]");
    Console.WriteLine();
    Console.WriteLine("Arguments:");
    Console.WriteLine("  audio-file          Path to local audio file (MP3, WAV, etc.)");
    Console.WriteLine("  output-file         (Optional) Output directory or file base name.");
    Console.WriteLine("                      Defaults to ./data/output/<input-name>");
    Console.WriteLine();
    Console.WriteLine("Options:");
    Console.WriteLine("  --batch             Use Azure Batch Transcription (uploads to Blob Storage).");
    Console.WriteLine("                      Faster for long recordings. Requires AzureBlob config.");
    Console.WriteLine("                      Without this flag, uses real-time SDK transcription.");
    return 1;
}

string audioInput = positionalArgs[0];
string? outputFile = positionalArgs.Length > 1 ? positionalArgs[1] : null;

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
services.Configure<AzureBlobSettings>(
    configuration.GetSection(AzureBlobSettings.SectionName));

services.AddLogging(builder =>
{
    builder.AddConsole();
    builder.SetMinimumLevel(LogLevel.Debug);
});

if (useBatch)
{
    Console.WriteLine("Mode: Batch Transcription (Blob Storage upload)");
    services.AddHttpClient<ITranscriptionService, AzureBatchTranscriptionService>();
}
else
{
    Console.WriteLine("Mode: Real-time SDK Transcription (local file)");
    services.AddSingleton<ITranscriptionService, AzureSpeechSdkTranscriptionService>();
}

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

    string outputDir = outputFile != null
        ? Path.GetDirectoryName(Path.GetFullPath(outputFile))!
        : Path.Combine(Directory.GetCurrentDirectory(), "data", "output");
    Directory.CreateDirectory(outputDir);

    string baseName = outputFile != null
        ? Path.GetFileNameWithoutExtension(outputFile)
        : Path.GetFileNameWithoutExtension(audioInput);

    string summaryPath = Path.Combine(outputDir, $"{baseName}.summary.txt");
    string transcriptionPath = Path.Combine(outputDir, $"{baseName}.transcription.txt");

    await File.WriteAllTextAsync(summaryPath, summary.Summary);
    await File.WriteAllTextAsync(transcriptionPath, transcription);

    Console.WriteLine("=== SUMMARY ===");
    Console.WriteLine();
    Console.WriteLine(summary.Summary);
    Console.WriteLine();
    Console.WriteLine($"Summary saved to: {summaryPath}");
    Console.WriteLine($"Transcription saved to: {transcriptionPath}");

    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Error: {ex.Message}");
    return 1;
}

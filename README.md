# DndTranscriber

A command-line tool that transcribes D&D session audio recordings and generates concise session summaries using Azure Speech Services and Anthropic's Claude.

## Prerequisites

- [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- [FFmpeg](https://ffmpeg.org/) (required on macOS for audio chunking)
- An [Azure Speech Services](https://azure.microsoft.com/en-us/products/ai-services/speech-services) API key
- An [Anthropic](https://www.anthropic.com/) API key

## Configuration

API keys can be provided via any of the following methods (listed in priority order):

### Option 1: User Secrets (recommended for development)

```bash
cd src/DndTranscriber
dotnet user-secrets set "AzureSpeech:SpeechApiKey" "your-azure-speech-key"
dotnet user-secrets set "Anthropic:AnthropicApiKey" "your-anthropic-key"
```

### Option 2: Environment Variables

```bash
export DNDTRANSCRIBER_AzureSpeech__SpeechApiKey="your-azure-speech-key"
export DNDTRANSCRIBER_Anthropic__AnthropicApiKey="your-anthropic-key"
```

## Building

```bash
dotnet build
```

## Running

From the repository root:

```bash
dotnet run --project src/DndTranscriber -- <audio-file> [output-file]
```

### Arguments

| Argument | Required | Description |
|---|---|---|
| `audio-file` | Yes | Path to a local audio file (MP3, WAV, etc.) |
| `output-file` | No | Path to save the summary. If omitted, prints to console. |

### Examples

Transcribe and print the summary to the console:

```bash
dotnet run --project src/DndTranscriber -- ./data/session1.mp3
```

Transcribe and save the summary to a file:

```bash
dotnet run --project src/DndTranscriber -- ./data/session1.mp3 ./data/session1-summary.md
```

### Output

- The session **summary** is printed to the console (and optionally saved to the specified output file).
- The full **transcription** is always saved to a file — either `<output-file-stem>.transcription.txt` (if an output file is specified) or `transcription.txt` in the current directory.

## Running Tests

```bash
dotnet test
```

## Architecture

The solution is organized into three projects:

- **DndTranscriber** — Console application entry point. Wires up configuration and dependency injection.
- **DndTranscriber.Core** — Domain layer with interfaces, models, and the `TranscriptionOrchestrator` that coordinates the pipeline.
- **DndTranscriber.DataAccess** — Infrastructure implementations:
  - **Audio chunking**: FFmpeg (macOS) or NAudio (Windows/Linux)
  - **Transcription**: Azure Speech SDK
  - **Summarization**: Anthropic Claude API

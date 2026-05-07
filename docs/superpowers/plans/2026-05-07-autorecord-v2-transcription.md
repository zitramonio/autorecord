# Autorecord v2 Local Transcription Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add local, GUI-managed transcription with model download, queueing, diarization, and transcript export while preserving the current MP3 recording behavior.

**Architecture:** Keep recording unchanged and attach transcription only to `RecordingCoordinator.RecordingSaved`. Build transcription as a separate core pipeline with catalog/model management, a persisted single-worker queue, local ASR/diarization engines, and WPF bindings in the existing app shell.

**Tech Stack:** .NET 10 WPF, NAudio, Ical.Net, TaskScheduler, xUnit, sherpa-onnx NuGet, System.Text.Json, Windows AppData storage.

---

## Scope Check

The v2 spec contains several subsystems: settings/UI, model catalog, model download, queue/history, transcript export, sherpa ASR, diarization, GigaAM worker integration, and recording integration. Implement in phases A-G. Each phase must build and test independently before moving to the next.

Do not change the current recording output format. The app continues saving recordings as `.mp3`; transcription creates a temporary normalized `.wav` internally.

## File Structure

### Core Settings

- Modify: `src/Autorecord.Core/Settings/AppSettings.cs`
  - Add `TranscriptionSettings`, `TranscriptOutputFolderMode`, and `TranscriptOutputFormat`.
- Modify: `src/Autorecord.Core/Settings/SettingsStore.cs`
  - Keep existing validation and add transcription validation.
- Test: `tests/Autorecord.Core.Tests/SettingsStoreTests.cs`
  - Add backward compatibility and transcription round-trip tests.

### Models

- Create: `models/catalog.json`
  - Extensible catalog for ASR and diarization models.
- Create: `src/Autorecord.Core/Transcription/Models/ModelCatalogEntry.cs`
- Create: `src/Autorecord.Core/Transcription/Models/ModelDownloadInfo.cs`
- Create: `src/Autorecord.Core/Transcription/Models/ModelInstallInfo.cs`
- Create: `src/Autorecord.Core/Transcription/Models/ModelRuntimeInfo.cs`
- Create: `src/Autorecord.Core/Transcription/Models/InstalledModelManifest.cs`
- Create: `src/Autorecord.Core/Transcription/Models/InstalledModelManifestEntry.cs`
- Create: `src/Autorecord.Core/Transcription/Models/ModelInstallStatus.cs`
- Create: `src/Autorecord.Core/Transcription/Models/ModelCatalog.cs`
- Create: `src/Autorecord.Core/Transcription/Models/ModelManager.cs`
- Create: `src/Autorecord.Core/Transcription/Models/ModelDownloadService.cs`
- Create: `src/Autorecord.Core/Transcription/Models/ModelDownloadProgress.cs`
- Test: `tests/Autorecord.Core.Tests/ModelCatalogTests.cs`
- Test: `tests/Autorecord.Core.Tests/ModelManagerTests.cs`
- Test: `tests/Autorecord.Core.Tests/ModelDownloadServiceTests.cs`

### Jobs and Queue

- Create: `src/Autorecord.Core/Transcription/Jobs/TranscriptionJob.cs`
- Create: `src/Autorecord.Core/Transcription/Jobs/TranscriptionJobStatus.cs`
- Create: `src/Autorecord.Core/Transcription/Jobs/TranscriptionJobRepository.cs`
- Create: `src/Autorecord.Core/Transcription/Jobs/TranscriptionQueue.cs`
- Create: `src/Autorecord.Core/Transcription/Jobs/TranscriptionJobLogWriter.cs`
- Test: `tests/Autorecord.Core.Tests/TranscriptionJobRepositoryTests.cs`
- Test: `tests/Autorecord.Core.Tests/TranscriptionQueueTests.cs`

### Pipeline and Export

- Create: `src/Autorecord.Core/Transcription/Pipeline/TranscriptionPipeline.cs`
- Create: `src/Autorecord.Core/Transcription/Pipeline/TranscriptionPipelineResult.cs`
- Create: `src/Autorecord.Core/Transcription/Pipeline/AudioNormalizer.cs`
- Create: `src/Autorecord.Core/Transcription/Pipeline/TranscriptAssembler.cs`
- Create: `src/Autorecord.Core/Transcription/Pipeline/LocalNetworkGuard.cs`
- Create: `src/Autorecord.Core/Transcription/Results/TranscriptDocument.cs`
- Create: `src/Autorecord.Core/Transcription/Results/TranscriptSegment.cs`
- Create: `src/Autorecord.Core/Transcription/Results/TranscriptSpeaker.cs`
- Create: `src/Autorecord.Core/Transcription/Results/DiarizationTurn.cs`
- Create: `src/Autorecord.Core/Transcription/Results/TranscriptExporter.cs`
- Create: `src/Autorecord.Core/Transcription/Results/TranscriptOutputFiles.cs`
- Test: `tests/Autorecord.Core.Tests/TranscriptExporterTests.cs`
- Test: `tests/Autorecord.Core.Tests/TranscriptAssemblerTests.cs`
- Test: `tests/Autorecord.Core.Tests/AudioNormalizerTests.cs`

### Engines

- Create: `src/Autorecord.Core/Transcription/Engines/ITranscriptionEngine.cs`
- Create: `src/Autorecord.Core/Transcription/Engines/TranscriptionEngineResult.cs`
- Create: `src/Autorecord.Core/Transcription/Engines/TranscriptionEngineSegment.cs`
- Create: `src/Autorecord.Core/Transcription/Engines/SherpaOnnxTranscriptionEngine.cs`
- Create: `src/Autorecord.Core/Transcription/Engines/GigaAmV3TranscriptionEngine.cs`
- Create: `src/Autorecord.Core/Transcription/Engines/GigaAmWorkerClient.cs`
- Create: `src/Autorecord.Core/Transcription/Diarization/IDiarizationEngine.cs`
- Create: `src/Autorecord.Core/Transcription/Diarization/DiarizationEngine.cs`
- Test: `tests/Autorecord.Core.Tests/SherpaOnnxTranscriptionEngineTests.cs`
- Test: `tests/Autorecord.Core.Tests/GigaAmWorkerClientTests.cs`
- Test: `tests/Autorecord.Core.Tests/DiarizationEngineTests.cs`

### WPF App

- Modify: `src/Autorecord.App/MainWindow.xaml`
  - Add tab layout and transcription controls.
- Modify: `src/Autorecord.App/MainWindow.xaml.cs`
  - Add events for transcription settings, model actions, queue actions, and file picking.
- Modify: `src/Autorecord.App/App.xaml.cs`
  - Wire catalog, model manager, queue, pipeline, notifications, and `RecordingSaved` integration.
- Create: `src/Autorecord.App/Notifications/TranscriptionNotificationService.cs`
- Create: `src/Autorecord.App/Transcription/TranscriptionSettingsViewModel.cs`
- Create: `src/Autorecord.App/Transcription/TranscriptionJobViewModel.cs`
- Create: `src/Autorecord.App/Transcription/ModelListItemViewModel.cs`

---

## Phase A: Data, Settings, Catalog, and UI Skeleton

### Task 1: Add Transcription Settings Types

**Files:**
- Modify: `src/Autorecord.Core/Settings/AppSettings.cs`
- Test: `tests/Autorecord.Core.Tests/SettingsStoreTests.cs`

- [ ] **Step 1: Add failing settings round-trip test**

Add this test to `tests/Autorecord.Core.Tests/SettingsStoreTests.cs`:

```csharp
[Fact]
public async Task SaveAndLoadRoundTripsTranscriptionSettings()
{
    var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "settings.json");
    var store = new SettingsStore(path);
    var settings = new AppSettings
    {
        Transcription = new TranscriptionSettings
        {
            AutoTranscribeAfterRecording = true,
            SelectedAsrModelId = "sherpa-gigaam-v2-ru-fast",
            SelectedDiarizationModelId = "sherpa-diarization-pyannote-fast",
            EnableDiarization = true,
            NumSpeakers = 3,
            ClusterThreshold = 0.65,
            OutputFolderMode = TranscriptOutputFolderMode.CustomFolder,
            CustomOutputFolder = "C:\\Transcripts",
            OutputFormats = [TranscriptOutputFormat.Txt, TranscriptOutputFormat.Markdown, TranscriptOutputFormat.Srt, TranscriptOutputFormat.Json],
            OverwriteExistingTranscripts = true,
            KeepIntermediateFiles = true
        }
    };

    await store.SaveAsync(settings, CancellationToken.None);
    var loaded = await store.LoadAsync(CancellationToken.None);

    Assert.Equal(settings.Transcription, loaded.Transcription);
}
```

- [ ] **Step 2: Run test to verify failure**

Run:

```powershell
& 'C:\Users\User\.dotnet\dotnet.exe' test .\tests\Autorecord.Core.Tests\Autorecord.Core.Tests.csproj --filter SaveAndLoadRoundTripsTranscriptionSettings --no-restore
```

Expected: fail because transcription settings types do not exist.

- [ ] **Step 3: Add settings types**

Modify `src/Autorecord.Core/Settings/AppSettings.cs`:

```csharp
namespace Autorecord.Core.Settings;

public enum RecordingMode
{
    AllEvents = 0,
    TaggedEvents = 1
}

public enum TranscriptOutputFolderMode
{
    SameAsRecording = 0,
    CustomFolder = 1
}

public enum TranscriptOutputFormat
{
    Txt = 0,
    Markdown = 1,
    Srt = 2,
    Json = 3
}

public sealed record TranscriptionSettings
{
    public bool AutoTranscribeAfterRecording { get; init; }
    public string SelectedAsrModelId { get; init; } = "sherpa-gigaam-v2-ru-fast";
    public string SelectedDiarizationModelId { get; init; } = "sherpa-diarization-pyannote-fast";
    public TranscriptOutputFolderMode OutputFolderMode { get; init; } = TranscriptOutputFolderMode.SameAsRecording;
    public string? CustomOutputFolder { get; init; }
    public IReadOnlyList<TranscriptOutputFormat> OutputFormats { get; init; } =
        [TranscriptOutputFormat.Txt, TranscriptOutputFormat.Markdown, TranscriptOutputFormat.Srt, TranscriptOutputFormat.Json];
    public bool EnableDiarization { get; init; }
    public int? NumSpeakers { get; init; }
    public double? ClusterThreshold { get; init; } = 0.65;
    public bool OverwriteExistingTranscripts { get; init; }
    public bool KeepIntermediateFiles { get; init; }
}

public sealed record AppSettings
{
    public string CalendarUrl { get; init; } = "";
    public string OutputFolder { get; init; } = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
    public RecordingMode RecordingMode { get; init; } = RecordingMode.AllEvents;
    public string EventTag { get; init; } = "record";
    public int SilencePromptMinutes { get; init; } = 1;
    public int RetryPromptMinutes { get; init; } = 5;
    public bool KeepMicrophoneReady { get; init; } = true;
    public bool StartWithWindows { get; init; }
    public TranscriptionSettings Transcription { get; init; } = new();
}
```

- [ ] **Step 4: Add validation tests**

Add tests:

```csharp
[Fact]
public async Task SaveRejectsCustomTranscriptFolderWithoutPath()
{
    var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "settings.json");
    var store = new SettingsStore(path);
    var settings = new AppSettings
    {
        Transcription = new TranscriptionSettings
        {
            OutputFolderMode = TranscriptOutputFolderMode.CustomFolder,
            CustomOutputFolder = ""
        }
    };

    await Assert.ThrowsAsync<ArgumentException>(() => store.SaveAsync(settings, CancellationToken.None));
}

[Fact]
public async Task SaveRejectsInvalidSpeakerCount()
{
    var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "settings.json");
    var store = new SettingsStore(path);
    var settings = new AppSettings
    {
        Transcription = new TranscriptionSettings { NumSpeakers = 7 }
    };

    await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => store.SaveAsync(settings, CancellationToken.None));
}
```

- [ ] **Step 5: Implement validation**

Modify `SettingsStore.Validate`:

```csharp
private static void Validate(AppSettings settings)
{
    if (settings.SilencePromptMinutes <= 0)
    {
        throw new ArgumentOutOfRangeException(nameof(settings), "Silence prompt interval must be positive.");
    }

    if (settings.RetryPromptMinutes <= 0)
    {
        throw new ArgumentOutOfRangeException(nameof(settings), "Retry prompt interval must be positive.");
    }

    if (!Enum.IsDefined(settings.RecordingMode))
    {
        throw new ArgumentOutOfRangeException(nameof(settings), "Recording mode is not supported.");
    }

    var transcription = settings.Transcription;
    if (!Enum.IsDefined(transcription.OutputFolderMode))
    {
        throw new ArgumentOutOfRangeException(nameof(settings), "Transcript output folder mode is not supported.");
    }

    if (transcription.OutputFolderMode == TranscriptOutputFolderMode.CustomFolder &&
        string.IsNullOrWhiteSpace(transcription.CustomOutputFolder))
    {
        throw new ArgumentException("Custom transcript output folder is required.", nameof(settings));
    }

    if (transcription.NumSpeakers is < 1 or > 6)
    {
        throw new ArgumentOutOfRangeException(nameof(settings), "Speaker count must be from 1 to 6.");
    }

    if (transcription.ClusterThreshold is <= 0 or > 1)
    {
        throw new ArgumentOutOfRangeException(nameof(settings), "Cluster threshold must be greater than 0 and at most 1.");
    }

    if (transcription.OutputFormats.Count == 0)
    {
        throw new ArgumentException("At least one transcript output format is required.", nameof(settings));
    }
}
```

- [ ] **Step 6: Run tests**

Run:

```powershell
& 'C:\Users\User\.dotnet\dotnet.exe' test .\tests\Autorecord.Core.Tests\Autorecord.Core.Tests.csproj --filter SettingsStoreTests --no-restore
```

Expected: all `SettingsStoreTests` pass.

- [ ] **Step 7: Commit**

```powershell
git add src/Autorecord.Core/Settings tests/Autorecord.Core.Tests/SettingsStoreTests.cs
git commit -m "feat: add transcription settings"
```

### Task 2: Add Model Catalog Data and JSON Catalog

**Files:**
- Create: `models/catalog.json`
- Create: `src/Autorecord.Core/Transcription/Models/ModelCatalogEntry.cs`
- Create: `src/Autorecord.Core/Transcription/Models/ModelDownloadInfo.cs`
- Create: `src/Autorecord.Core/Transcription/Models/ModelInstallInfo.cs`
- Create: `src/Autorecord.Core/Transcription/Models/ModelRuntimeInfo.cs`
- Create: `src/Autorecord.Core/Transcription/Models/ModelCatalog.cs`
- Test: `tests/Autorecord.Core.Tests/ModelCatalogTests.cs`

- [ ] **Step 1: Create failing model catalog test**

Create `tests/Autorecord.Core.Tests/ModelCatalogTests.cs`:

```csharp
using Autorecord.Core.Transcription.Models;

namespace Autorecord.Core.Tests;

public sealed class ModelCatalogTests
{
    [Fact]
    public async Task LoadAsyncLoadsAsrAndDiarizationModels()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "catalog.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, """
        [
          {
            "id": "sherpa-gigaam-v2-ru-fast",
            "displayName": "Русский — быстро",
            "description": "Быстрая локальная транскрибация.",
            "type": "asr",
            "engine": "sherpa-onnx",
            "language": "ru",
            "version": "2025-04-19",
            "sizeMb": 250,
            "requiresDiarization": false,
            "download": { "url": "https://example.com/model.tar.bz2", "archiveType": "tar.bz2", "sha256": null },
            "install": { "targetFolder": "sherpa-gigaam-v2-ru-fast", "requiredFiles": ["model.int8.onnx", "tokens.txt"] },
            "runtime": { "sampleRate": 16000, "channels": 1, "device": "cpu" }
          },
          {
            "id": "sherpa-diarization-pyannote-fast",
            "displayName": "Спикеры — быстро",
            "description": "Быстрая локальная диаризация.",
            "type": "diarization",
            "engine": "sherpa-onnx",
            "language": "any",
            "version": "pyannote-segmentation-3-0-int8",
            "sizeMb": 50,
            "download": { "segmentationUrl": "https://example.com/segmentation.tar.bz2", "embeddingUrl": "https://example.com/embedding.onnx", "sha256": null },
            "install": { "targetFolder": "sherpa-diarization-pyannote-fast", "requiredFiles": ["model.int8.onnx", "embedding.onnx"] },
            "runtime": { "sampleRate": 16000, "channels": 1, "device": "cpu" }
          }
        ]
        """);

        var catalog = await ModelCatalog.LoadAsync(path, CancellationToken.None);

        Assert.Equal(2, catalog.Models.Count);
        Assert.Single(catalog.GetByType("asr"));
        Assert.Single(catalog.GetByType("diarization"));
        Assert.Equal("Русский — быстро", catalog.GetRequired("sherpa-gigaam-v2-ru-fast").DisplayName);
    }
}
```

- [ ] **Step 2: Run test to verify failure**

```powershell
& 'C:\Users\User\.dotnet\dotnet.exe' test .\tests\Autorecord.Core.Tests\Autorecord.Core.Tests.csproj --filter ModelCatalogTests --no-restore
```

Expected: fail because `ModelCatalog` does not exist.

- [ ] **Step 3: Add model catalog types**

Create `src/Autorecord.Core/Transcription/Models/ModelCatalogEntry.cs`:

```csharp
namespace Autorecord.Core.Transcription.Models;

public sealed record ModelCatalogEntry
{
    public string Id { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public string Description { get; init; } = "";
    public string Type { get; init; } = "";
    public string Engine { get; init; } = "";
    public string Language { get; init; } = "";
    public string Version { get; init; } = "";
    public int? SizeMb { get; init; }
    public bool RequiresDiarization { get; init; }
    public ModelDownloadInfo Download { get; init; } = new();
    public ModelInstallInfo Install { get; init; } = new();
    public ModelRuntimeInfo Runtime { get; init; } = new();
}
```

Create `ModelDownloadInfo.cs`:

```csharp
namespace Autorecord.Core.Transcription.Models;

public sealed record ModelDownloadInfo
{
    public string? Url { get; init; }
    public string? SegmentationUrl { get; init; }
    public string? EmbeddingUrl { get; init; }
    public string? ArchiveType { get; init; }
    public string? Sha256 { get; init; }
}
```

Create `ModelInstallInfo.cs`:

```csharp
namespace Autorecord.Core.Transcription.Models;

public sealed record ModelInstallInfo
{
    public string TargetFolder { get; init; } = "";
    public IReadOnlyList<string> RequiredFiles { get; init; } = [];
}
```

Create `ModelRuntimeInfo.cs`:

```csharp
namespace Autorecord.Core.Transcription.Models;

public sealed record ModelRuntimeInfo
{
    public int SampleRate { get; init; } = 16000;
    public int Channels { get; init; } = 1;
    public string Device { get; init; } = "cpu";
}
```

- [ ] **Step 4: Add ModelCatalog**

Create `src/Autorecord.Core/Transcription/Models/ModelCatalog.cs`:

```csharp
using System.Text.Json;

namespace Autorecord.Core.Transcription.Models;

public sealed class ModelCatalog
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly Dictionary<string, ModelCatalogEntry> _byId;

    private ModelCatalog(IReadOnlyList<ModelCatalogEntry> models)
    {
        Models = models;
        _byId = models.ToDictionary(model => model.Id, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<ModelCatalogEntry> Models { get; }

    public static async Task<ModelCatalog> LoadAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var models = await JsonSerializer.DeserializeAsync<List<ModelCatalogEntry>>(stream, JsonOptions, cancellationToken)
            ?? [];
        Validate(models);
        return new ModelCatalog(models);
    }

    public IEnumerable<ModelCatalogEntry> GetByType(string type)
    {
        return Models.Where(model => string.Equals(model.Type, type, StringComparison.OrdinalIgnoreCase));
    }

    public ModelCatalogEntry GetRequired(string id)
    {
        if (_byId.TryGetValue(id, out var model))
        {
            return model;
        }

        throw new InvalidOperationException($"Model is not found in catalog: {id}");
    }

    private static void Validate(IReadOnlyList<ModelCatalogEntry> models)
    {
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var model in models)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(model.Id);
            ArgumentException.ThrowIfNullOrWhiteSpace(model.DisplayName);
            ArgumentException.ThrowIfNullOrWhiteSpace(model.Type);
            ArgumentException.ThrowIfNullOrWhiteSpace(model.Engine);
            ArgumentException.ThrowIfNullOrWhiteSpace(model.Install.TargetFolder);
            if (!ids.Add(model.Id))
            {
                throw new InvalidOperationException($"Duplicate model id: {model.Id}");
            }
        }
    }
}
```

- [ ] **Step 5: Add production catalog**

Create `models/catalog.json` with the four model entries from the approved spec. Keep `sha256` as `null` until verified checksums are available. Use the exact ids:

```json
[
  {
    "id": "sherpa-gigaam-v2-ru-fast",
    "displayName": "Русский — быстро",
    "description": "Быстрая локальная транскрибация русской речи через sherpa-onnx и GigaAM v2 ONNX.",
    "type": "asr",
    "engine": "sherpa-onnx",
    "language": "ru",
    "version": "2025-04-19",
    "sizeMb": 250,
    "requiresDiarization": false,
    "download": {
      "url": "https://github.com/k2-fsa/sherpa-onnx/releases/download/asr-models/sherpa-onnx-nemo-ctc-giga-am-v2-russian-2025-04-19.tar.bz2",
      "archiveType": "tar.bz2",
      "sha256": null
    },
    "install": {
      "targetFolder": "sherpa-gigaam-v2-ru-fast",
      "requiredFiles": ["model.int8.onnx", "tokens.txt"]
    },
    "runtime": { "sampleRate": 16000, "channels": 1, "device": "cpu" }
  },
  {
    "id": "gigaam-v3-ru-quality",
    "displayName": "Русский — качество",
    "description": "Качественная локальная транскрибация русской речи через GigaAM v3.",
    "type": "asr",
    "engine": "gigaam-v3",
    "language": "ru",
    "version": "v3",
    "sizeMb": null,
    "requiresDiarization": false,
    "download": { "url": null, "archiveType": null, "sha256": null },
    "install": { "targetFolder": "gigaam-v3-ru-quality", "requiredFiles": [] },
    "runtime": { "sampleRate": 16000, "channels": 1, "device": "cpu" }
  },
  {
    "id": "sherpa-diarization-pyannote-fast",
    "displayName": "Спикеры — быстро",
    "description": "Локальное разделение реплик по спикерам через sherpa-onnx diarization int8.",
    "type": "diarization",
    "engine": "sherpa-onnx",
    "language": "any",
    "version": "pyannote-segmentation-3-0-int8",
    "sizeMb": 50,
    "download": {
      "segmentationUrl": "https://github.com/k2-fsa/sherpa-onnx/releases/download/speaker-segmentation-models/sherpa-onnx-pyannote-segmentation-3-0.tar.bz2",
      "embeddingUrl": "https://github.com/k2-fsa/sherpa-onnx/releases/download/speaker-recongition-models/3dspeaker_speech_eres2net_base_sv_zh-cn_3dspeaker_16k.onnx",
      "sha256": null
    },
    "install": {
      "targetFolder": "sherpa-diarization-pyannote-fast",
      "requiredFiles": ["model.int8.onnx", "3dspeaker_speech_eres2net_base_sv_zh-cn_3dspeaker_16k.onnx"]
    },
    "runtime": { "sampleRate": 16000, "channels": 1, "device": "cpu" }
  },
  {
    "id": "sherpa-diarization-pyannote-quality",
    "displayName": "Спикеры — качество",
    "description": "Локальное разделение реплик по спикерам через sherpa-onnx diarization fp32.",
    "type": "diarization",
    "engine": "sherpa-onnx",
    "language": "any",
    "version": "pyannote-segmentation-3-0-fp32",
    "sizeMb": 60,
    "download": {
      "segmentationUrl": "https://github.com/k2-fsa/sherpa-onnx/releases/download/speaker-segmentation-models/sherpa-onnx-pyannote-segmentation-3-0.tar.bz2",
      "embeddingUrl": "https://github.com/k2-fsa/sherpa-onnx/releases/download/speaker-recongition-models/3dspeaker_speech_eres2net_base_sv_zh-cn_3dspeaker_16k.onnx",
      "sha256": null
    },
    "install": {
      "targetFolder": "sherpa-diarization-pyannote-quality",
      "requiredFiles": ["model.onnx", "3dspeaker_speech_eres2net_base_sv_zh-cn_3dspeaker_16k.onnx"]
    },
    "runtime": { "sampleRate": 16000, "channels": 1, "device": "cpu" }
  }
]
```

- [ ] **Step 6: Run tests and build**

```powershell
& 'C:\Users\User\.dotnet\dotnet.exe' test .\tests\Autorecord.Core.Tests\Autorecord.Core.Tests.csproj --filter ModelCatalogTests --no-restore
& 'C:\Users\User\.dotnet\dotnet.exe' build .\Autorecord.sln --no-restore
```

Expected: tests pass and build succeeds.

- [ ] **Step 7: Commit**

```powershell
git add models src/Autorecord.Core/Transcription/Models tests/Autorecord.Core.Tests/ModelCatalogTests.cs
git commit -m "feat: add transcription model catalog"
```

### Task 3: Add Transcription Tab Skeleton

**Files:**
- Modify: `src/Autorecord.App/MainWindow.xaml`
- Modify: `src/Autorecord.App/MainWindow.xaml.cs`
- Create: `src/Autorecord.App/Transcription/TranscriptionSettingsViewModel.cs`
- Create: `src/Autorecord.App/Transcription/ModelListItemViewModel.cs`

- [ ] **Step 1: Add ViewModel records**

Create `src/Autorecord.App/Transcription/ModelListItemViewModel.cs`:

```csharp
namespace Autorecord.App.Transcription;

public sealed record ModelListItemViewModel(string Id, string DisplayName, string Type, string Status);
```

Create `src/Autorecord.App/Transcription/TranscriptionSettingsViewModel.cs`:

```csharp
using Autorecord.Core.Settings;

namespace Autorecord.App.Transcription;

public sealed record TranscriptionSettingsViewModel
{
    public bool AutoTranscribeAfterRecording { get; init; }
    public string SelectedAsrModelId { get; init; } = "";
    public string SelectedDiarizationModelId { get; init; } = "";
    public bool EnableDiarization { get; init; }
    public int? NumSpeakers { get; init; }
    public TranscriptOutputFolderMode OutputFolderMode { get; init; }
    public string? CustomOutputFolder { get; init; }
    public IReadOnlyList<TranscriptOutputFormat> OutputFormats { get; init; } = [];
}
```

- [ ] **Step 2: Replace MainWindow root Grid with TabControl**

Modify `MainWindow.xaml` so the existing recording/settings UI is moved into a `TabItem Header="Запись"`, and add a second `TabItem Header="Транскрибация"` with named controls:

```xml
<TabControl>
    <TabItem Header="Запись">
        <!-- existing recording/settings grid goes here unchanged except for nesting -->
    </TabItem>
    <TabItem Header="Транскрибация">
        <Grid Margin="16">
            <Grid.RowDefinitions>
                <RowDefinition Height="Auto" />
                <RowDefinition Height="Auto" />
                <RowDefinition Height="Auto" />
                <RowDefinition />
                <RowDefinition Height="Auto" />
            </Grid.RowDefinitions>

            <CheckBox x:Name="AutoTranscribeBox"
                      Content="Транскрибировать записи автоматически" />

            <Grid Grid.Row="1" Margin="0,12,0,0">
                <Grid.ColumnDefinitions>
                    <ColumnDefinition />
                    <ColumnDefinition />
                </Grid.ColumnDefinitions>
                <StackPanel>
                    <TextBlock Text="ASR-модель" />
                    <ComboBox x:Name="AsrModelBox"
                              DisplayMemberPath="DisplayName"
                              SelectedValuePath="Id"
                              Margin="0,6,8,0" />
                </StackPanel>
                <StackPanel Grid.Column="1">
                    <TextBlock Text="Диаризация" />
                    <ComboBox x:Name="DiarizationModeBox" Margin="0,6,0,0" />
                </StackPanel>
            </Grid>

            <StackPanel Grid.Row="2" Margin="0,12,0,0">
                <TextBlock x:Name="SelectedModelStatusText" Text="Модель не установлена" />
                <ProgressBar x:Name="ModelDownloadProgress"
                             Height="16"
                             Minimum="0"
                             Maximum="100"
                             Value="0"
                             Margin="0,8,0,8" />
                <StackPanel Orientation="Horizontal">
                    <Button x:Name="DownloadModelButton" Width="120" Click="DownloadModel_Click">Скачать модель</Button>
                    <Button x:Name="DeleteModelButton" Width="120" Margin="8,0,0,0" Click="DeleteModel_Click">Удалить модель</Button>
                    <Button x:Name="ValidateModelButton" Width="120" Margin="8,0,0,0" Click="ValidateModel_Click">Проверить модель</Button>
                    <Button x:Name="OpenModelsFolderButton" Width="150" Margin="8,0,0,0" Click="OpenModelsFolder_Click">Открыть папку моделей</Button>
                </StackPanel>
            </StackPanel>

            <DataGrid x:Name="TranscriptionJobsGrid"
                      Grid.Row="3"
                      Margin="0,12,0,0"
                      AutoGenerateColumns="False"
                      IsReadOnly="True">
                <DataGrid.Columns>
                    <DataGridTextColumn Header="Файл" Binding="{Binding FileName}" />
                    <DataGridTextColumn Header="Модель" Binding="{Binding ModelName}" />
                    <DataGridTextColumn Header="Статус" Binding="{Binding Status}" />
                    <DataGridTextColumn Header="Прогресс" Binding="{Binding ProgressPercent}" />
                    <DataGridTextColumn Header="Создано" Binding="{Binding CreatedAt}" />
                    <DataGridTextColumn Header="Завершено" Binding="{Binding FinishedAt}" />
                </DataGrid.Columns>
            </DataGrid>

            <Button Grid.Row="4"
                    Width="220"
                    Margin="0,12,0,0"
                    HorizontalAlignment="Left"
                    Click="PickFileForTranscription_Click">
                Выбрать файл и транскрибировать
            </Button>
        </Grid>
    </TabItem>
</TabControl>
```

- [ ] **Step 3: Add MainWindow events**

Add events and empty handlers to `MainWindow.xaml.cs`:

```csharp
public event EventHandler? DownloadSelectedModelRequested;
public event EventHandler? DeleteSelectedModelRequested;
public event EventHandler? ValidateSelectedModelRequested;
public event EventHandler? OpenModelsFolderRequested;
public event EventHandler? PickFileForTranscriptionRequested;

private void DownloadModel_Click(object sender, RoutedEventArgs e) =>
    DownloadSelectedModelRequested?.Invoke(this, EventArgs.Empty);

private void DeleteModel_Click(object sender, RoutedEventArgs e) =>
    DeleteSelectedModelRequested?.Invoke(this, EventArgs.Empty);

private void ValidateModel_Click(object sender, RoutedEventArgs e) =>
    ValidateSelectedModelRequested?.Invoke(this, EventArgs.Empty);

private void OpenModelsFolder_Click(object sender, RoutedEventArgs e) =>
    OpenModelsFolderRequested?.Invoke(this, EventArgs.Empty);

private void PickFileForTranscription_Click(object sender, RoutedEventArgs e) =>
    PickFileForTranscriptionRequested?.Invoke(this, EventArgs.Empty);
```

- [ ] **Step 4: Run build**

```powershell
& 'C:\Users\User\.dotnet\dotnet.exe' build .\Autorecord.sln --no-restore
```

Expected: build succeeds.

- [ ] **Step 5: Commit**

```powershell
git add src/Autorecord.App/MainWindow.xaml src/Autorecord.App/MainWindow.xaml.cs src/Autorecord.App/Transcription
git commit -m "feat: add transcription tab skeleton"
```

---

## Phase B: ModelManager and ModelDownloadService

### Task 4: Implement ModelManager Manifest and Required File Checks

**Files:**
- Create: `src/Autorecord.Core/Transcription/Models/ModelInstallStatus.cs`
- Create: `src/Autorecord.Core/Transcription/Models/InstalledModelManifest.cs`
- Create: `src/Autorecord.Core/Transcription/Models/InstalledModelManifestEntry.cs`
- Create: `src/Autorecord.Core/Transcription/Models/ModelManager.cs`
- Test: `tests/Autorecord.Core.Tests/ModelManagerTests.cs`

- [ ] **Step 1: Write tests**

Create `ModelManagerTests.cs`:

```csharp
using Autorecord.Core.Transcription.Models;

namespace Autorecord.Core.Tests;

public sealed class ModelManagerTests
{
    [Fact]
    public async Task GetStatusAsyncReturnsInstalledWhenRequiredFilesExist()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var entry = CatalogEntry("model-a", ["model.onnx", "tokens.txt"]);
        Directory.CreateDirectory(Path.Combine(root, "model-a"));
        await File.WriteAllTextAsync(Path.Combine(root, "model-a", "model.onnx"), "");
        await File.WriteAllTextAsync(Path.Combine(root, "model-a", "tokens.txt"), "");
        var manager = new ModelManager(root);

        var status = await manager.GetStatusAsync(entry, CancellationToken.None);

        Assert.Equal(ModelInstallStatus.Installed, status);
    }

    [Fact]
    public async Task GetStatusAsyncReturnsMissingRequiredFilesWhenFileIsAbsent()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var entry = CatalogEntry("model-a", ["model.onnx", "tokens.txt"]);
        Directory.CreateDirectory(Path.Combine(root, "model-a"));
        await File.WriteAllTextAsync(Path.Combine(root, "model-a", "model.onnx"), "");
        var manager = new ModelManager(root);

        var status = await manager.GetStatusAsync(entry, CancellationToken.None);

        Assert.Equal(ModelInstallStatus.MissingRequiredFiles, status);
    }

    private static ModelCatalogEntry CatalogEntry(string id, IReadOnlyList<string> requiredFiles) => new()
    {
        Id = id,
        DisplayName = id,
        Type = "asr",
        Engine = "sherpa-onnx",
        Install = new ModelInstallInfo { TargetFolder = id, RequiredFiles = requiredFiles }
    };
}
```

- [ ] **Step 2: Implement status types and manager**

Create `ModelInstallStatus.cs`:

```csharp
namespace Autorecord.Core.Transcription.Models;

public enum ModelInstallStatus
{
    NotInstalled = 0,
    Downloading = 1,
    Installed = 2,
    MissingRequiredFiles = 3,
    DownloadUnavailable = 4,
    Error = 5
}
```

Create `ModelManager.cs`:

```csharp
namespace Autorecord.Core.Transcription.Models;

public sealed class ModelManager
{
    private readonly string _modelsRoot;

    public ModelManager(string modelsRoot)
    {
        _modelsRoot = modelsRoot;
    }

    public string ModelsRoot => _modelsRoot;

    public string GetModelPath(ModelCatalogEntry model)
    {
        return Path.Combine(_modelsRoot, model.Install.TargetFolder);
    }

    public Task<ModelInstallStatus> GetStatusAsync(ModelCatalogEntry model, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(model.Download.Url) &&
            string.IsNullOrWhiteSpace(model.Download.SegmentationUrl) &&
            string.IsNullOrWhiteSpace(model.Download.EmbeddingUrl) &&
            model.Install.RequiredFiles.Count == 0)
        {
            return Task.FromResult(ModelInstallStatus.DownloadUnavailable);
        }

        var modelPath = GetModelPath(model);
        if (!Directory.Exists(modelPath))
        {
            return Task.FromResult(ModelInstallStatus.NotInstalled);
        }

        foreach (var requiredFile in model.Install.RequiredFiles)
        {
            if (!File.Exists(Path.Combine(modelPath, requiredFile)))
            {
                return Task.FromResult(ModelInstallStatus.MissingRequiredFiles);
            }
        }

        return Task.FromResult(ModelInstallStatus.Installed);
    }

    public Task DeleteAsync(ModelCatalogEntry model, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var modelPath = GetModelPath(model);
        if (Directory.Exists(modelPath))
        {
            Directory.Delete(modelPath, recursive: true);
        }

        return Task.CompletedTask;
    }
}
```

- [ ] **Step 3: Run tests**

```powershell
& 'C:\Users\User\.dotnet\dotnet.exe' test .\tests\Autorecord.Core.Tests\Autorecord.Core.Tests.csproj --filter ModelManagerTests --no-restore
```

Expected: tests pass.

- [ ] **Step 4: Commit**

```powershell
git add src/Autorecord.Core/Transcription/Models tests/Autorecord.Core.Tests/ModelManagerTests.cs
git commit -m "feat: manage installed transcription models"
```

### Task 5: Implement Download Failure, Progress Contract, and Archive Hook

**Files:**
- Create: `src/Autorecord.Core/Transcription/Models/ModelDownloadProgress.cs`
- Create: `src/Autorecord.Core/Transcription/Models/ModelDownloadService.cs`
- Test: `tests/Autorecord.Core.Tests/ModelDownloadServiceTests.cs`

- [ ] **Step 1: Write failed download test**

```csharp
using System.Net;
using Autorecord.Core.Transcription.Models;

namespace Autorecord.Core.Tests;

public sealed class ModelDownloadServiceTests
{
    [Fact]
    public async Task DownloadAsyncThrowsClearErrorForServerFailure()
    {
        var client = new HttpClient(new StaticResponseHandler(HttpStatusCode.InternalServerError));
        var service = new ModelDownloadService(client, Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));
        var model = new ModelCatalogEntry
        {
            Id = "model-a",
            DisplayName = "Model A",
            Download = new ModelDownloadInfo { Url = "https://example.com/model.tar.bz2", ArchiveType = "tar.bz2" },
            Install = new ModelInstallInfo { TargetFolder = "model-a", RequiredFiles = ["model.onnx"] }
        };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.DownloadAsync(model, null, CancellationToken.None));

        Assert.Contains("download", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class StaticResponseHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _statusCode;

        public StaticResponseHandler(HttpStatusCode statusCode)
        {
            _statusCode = statusCode;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(_statusCode));
        }
    }
}
```

- [ ] **Step 2: Implement progress and download service shell**

Create `ModelDownloadProgress.cs`:

```csharp
namespace Autorecord.Core.Transcription.Models;

public sealed record ModelDownloadProgress(long BytesDownloaded, long? TotalBytes, double? BytesPerSecond)
{
    public int Percent => TotalBytes is > 0
        ? (int)Math.Clamp(BytesDownloaded * 100 / TotalBytes.Value, 0, 100)
        : 0;
}
```

Create `ModelDownloadService.cs`:

```csharp
using System.Diagnostics;

namespace Autorecord.Core.Transcription.Models;

public sealed class ModelDownloadService
{
    private readonly HttpClient _httpClient;
    private readonly string _downloadsRoot;

    public ModelDownloadService(HttpClient httpClient, string downloadsRoot)
    {
        _httpClient = httpClient;
        _downloadsRoot = downloadsRoot;
    }

    public async Task<string> DownloadAsync(
        ModelCatalogEntry model,
        IProgress<ModelDownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        var url = model.Download.Url ?? model.Download.SegmentationUrl;
        if (string.IsNullOrWhiteSpace(url))
        {
            throw new InvalidOperationException($"Model download is unavailable: {model.Id}");
        }

        Directory.CreateDirectory(_downloadsRoot);
        var tempPath = Path.Combine(_downloadsRoot, $"{model.Id}.{Guid.NewGuid():N}.download");

        try
        {
            using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException($"Model download failed with HTTP {(int)response.StatusCode}.");
            }

            var total = response.Content.Headers.ContentLength;
            await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var output = File.Create(tempPath);
            var buffer = new byte[1024 * 128];
            long downloaded = 0;
            var stopwatch = Stopwatch.StartNew();

            while (true)
            {
                var read = await input.ReadAsync(buffer, cancellationToken);
                if (read == 0)
                {
                    break;
                }

                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                downloaded += read;
                var speed = stopwatch.Elapsed.TotalSeconds > 0 ? downloaded / stopwatch.Elapsed.TotalSeconds : null;
                progress?.Report(new ModelDownloadProgress(downloaded, total, speed));
            }

            return tempPath;
        }
        catch
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }

            throw;
        }
    }
}
```

- [ ] **Step 3: Add success/progress test with in-memory content**

Add:

```csharp
[Fact]
public async Task DownloadAsyncWritesTempFileAndReportsProgress()
{
    var content = new byte[1024];
    Array.Fill<byte>(content, 7);
    var client = new HttpClient(new BytesResponseHandler(content));
    var service = new ModelDownloadService(client, Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));
    var reports = new List<ModelDownloadProgress>();
    var model = new ModelCatalogEntry
    {
        Id = "model-a",
        DisplayName = "Model A",
        Download = new ModelDownloadInfo { Url = "https://example.com/model.bin" },
        Install = new ModelInstallInfo { TargetFolder = "model-a", RequiredFiles = ["model.bin"] }
    };

    var path = await service.DownloadAsync(model, new Progress<ModelDownloadProgress>(reports.Add), CancellationToken.None);

    Assert.True(File.Exists(path));
    Assert.Equal(1024, new FileInfo(path).Length);
    Assert.NotEmpty(reports);
}

private sealed class BytesResponseHandler : HttpMessageHandler
{
    private readonly byte[] _content;

    public BytesResponseHandler(byte[] content)
    {
        _content = content;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(_content)
        };
        response.Content.Headers.ContentLength = _content.Length;
        return Task.FromResult(response);
    }
}
```

- [ ] **Step 4: Run tests**

```powershell
& 'C:\Users\User\.dotnet\dotnet.exe' test .\tests\Autorecord.Core.Tests\Autorecord.Core.Tests.csproj --filter ModelDownloadServiceTests --no-restore
```

Expected: tests pass.

- [ ] **Step 5: Commit**

```powershell
git add src/Autorecord.Core/Transcription/Models tests/Autorecord.Core.Tests/ModelDownloadServiceTests.cs
git commit -m "feat: download transcription models with progress"
```

---

## Phase C: Queue and Transcript Export

### Task 6: Add Job Repository

**Files:**
- Create: `src/Autorecord.Core/Transcription/Jobs/TranscriptionJobStatus.cs`
- Create: `src/Autorecord.Core/Transcription/Jobs/TranscriptionJob.cs`
- Create: `src/Autorecord.Core/Transcription/Jobs/TranscriptionJobRepository.cs`
- Test: `tests/Autorecord.Core.Tests/TranscriptionJobRepositoryTests.cs`

- [ ] **Step 1: Write repository tests**

```csharp
using Autorecord.Core.Transcription.Jobs;

namespace Autorecord.Core.Tests;

public sealed class TranscriptionJobRepositoryTests
{
    [Fact]
    public async Task SaveAndLoadRoundTripsJobs()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "jobs.json");
        var repository = new TranscriptionJobRepository(path);
        var job = new TranscriptionJob
        {
            Id = Guid.NewGuid(),
            InputFilePath = "C:\\Records\\meeting.mp3",
            OutputDirectory = "C:\\Transcripts",
            AsrModelId = "sherpa-gigaam-v2-ru-fast",
            DiarizationModelId = "sherpa-diarization-pyannote-fast",
            Status = TranscriptionJobStatus.Pending,
            CreatedAt = new DateTimeOffset(2026, 5, 7, 12, 0, 0, TimeSpan.Zero)
        };

        await repository.SaveAsync([job], CancellationToken.None);
        var loaded = await repository.LoadAsync(CancellationToken.None);

        Assert.Single(loaded);
        Assert.Equal(job, loaded[0]);
    }

    [Fact]
    public async Task LoadConvertsRunningJobsToPending()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "jobs.json");
        var repository = new TranscriptionJobRepository(path);
        var job = new TranscriptionJob
        {
            Id = Guid.NewGuid(),
            InputFilePath = "C:\\Records\\meeting.mp3",
            OutputDirectory = "C:\\Transcripts",
            AsrModelId = "model",
            Status = TranscriptionJobStatus.Running,
            CreatedAt = DateTimeOffset.Now,
            StartedAt = DateTimeOffset.Now
        };

        await repository.SaveAsync([job], CancellationToken.None);
        var loaded = await repository.LoadAsync(CancellationToken.None);

        Assert.Equal(TranscriptionJobStatus.Pending, loaded[0].Status);
        Assert.Contains("interrupted", loaded[0].ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }
}
```

- [ ] **Step 2: Implement job types**

Create `TranscriptionJobStatus.cs`:

```csharp
namespace Autorecord.Core.Transcription.Jobs;

public enum TranscriptionJobStatus
{
    Pending = 0,
    WaitingForModel = 1,
    Running = 2,
    Completed = 3,
    Failed = 4,
    Cancelled = 5
}
```

Create `TranscriptionJob.cs`:

```csharp
namespace Autorecord.Core.Transcription.Jobs;

public sealed record TranscriptionJob
{
    public Guid Id { get; init; }
    public string InputFilePath { get; init; } = "";
    public string OutputDirectory { get; init; } = "";
    public string AsrModelId { get; init; } = "";
    public string? DiarizationModelId { get; init; }
    public TranscriptionJobStatus Status { get; init; }
    public int ProgressPercent { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? StartedAt { get; init; }
    public DateTimeOffset? FinishedAt { get; init; }
    public string? ErrorMessage { get; init; }
    public IReadOnlyList<string> OutputFiles { get; init; } = [];
}
```

Create `TranscriptionJobRepository.cs`:

```csharp
using System.Text.Json;

namespace Autorecord.Core.Transcription.Jobs;

public sealed class TranscriptionJobRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string _path;

    public TranscriptionJobRepository(string path)
    {
        _path = path;
    }

    public async Task<IReadOnlyList<TranscriptionJob>> LoadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_path))
        {
            return [];
        }

        await using var stream = File.OpenRead(_path);
        var jobs = await JsonSerializer.DeserializeAsync<List<TranscriptionJob>>(stream, JsonOptions, cancellationToken)
            ?? [];

        return jobs
            .Select(job => job.Status == TranscriptionJobStatus.Running
                ? job with
                {
                    Status = TranscriptionJobStatus.Pending,
                    ErrorMessage = "Processing was interrupted when the application stopped.",
                    StartedAt = null
                }
                : job)
            .ToList();
    }

    public async Task SaveAsync(IReadOnlyList<TranscriptionJob> jobs, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using var stream = File.Create(_path);
        await JsonSerializer.SerializeAsync(stream, jobs, JsonOptions, cancellationToken);
    }
}
```

- [ ] **Step 3: Run tests**

```powershell
& 'C:\Users\User\.dotnet\dotnet.exe' test .\tests\Autorecord.Core.Tests\Autorecord.Core.Tests.csproj --filter TranscriptionJobRepositoryTests --no-restore
```

Expected: tests pass.

- [ ] **Step 4: Commit**

```powershell
git add src/Autorecord.Core/Transcription/Jobs tests/Autorecord.Core.Tests/TranscriptionJobRepositoryTests.cs
git commit -m "feat: persist transcription jobs"
```

### Task 7: Add Transcript Exporter

**Files:**
- Create: `src/Autorecord.Core/Transcription/Results/TranscriptDocument.cs`
- Create: `src/Autorecord.Core/Transcription/Results/TranscriptSegment.cs`
- Create: `src/Autorecord.Core/Transcription/Results/TranscriptSpeaker.cs`
- Create: `src/Autorecord.Core/Transcription/Results/DiarizationTurn.cs`
- Create: `src/Autorecord.Core/Transcription/Results/TranscriptOutputFiles.cs`
- Create: `src/Autorecord.Core/Transcription/Results/TranscriptExporter.cs`
- Test: `tests/Autorecord.Core.Tests/TranscriptExporterTests.cs`

- [ ] **Step 1: Write exporter tests**

Create `TranscriptExporterTests.cs` with four tests:

```csharp
using Autorecord.Core.Settings;
using Autorecord.Core.Transcription.Results;

namespace Autorecord.Core.Tests;

public sealed class TranscriptExporterTests
{
    [Fact]
    public async Task ExportAsyncCreatesTxtMdSrtAndJson()
    {
        var output = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var document = SampleDocument(output);
        var exporter = new TranscriptExporter();

        var files = await exporter.ExportAsync(
            document,
            output,
            [TranscriptOutputFormat.Txt, TranscriptOutputFormat.Markdown, TranscriptOutputFormat.Srt, TranscriptOutputFormat.Json],
            overwrite: false,
            CancellationToken.None);

        Assert.True(File.Exists(files.TxtPath));
        Assert.True(File.Exists(files.MarkdownPath));
        Assert.True(File.Exists(files.SrtPath));
        Assert.True(File.Exists(files.JsonPath));
        Assert.Contains("Speaker 1", await File.ReadAllTextAsync(files.TxtPath!));
        Assert.Contains("00:00:01,200 --> 00:00:06,800", await File.ReadAllTextAsync(files.SrtPath!));
    }

    [Fact]
    public async Task ExportAsyncAddsSuffixWhenFilesExist()
    {
        var output = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(output);
        await File.WriteAllTextAsync(Path.Combine(output, "meeting.txt"), "existing");
        var exporter = new TranscriptExporter();

        var files = await exporter.ExportAsync(
            SampleDocument(output),
            output,
            [TranscriptOutputFormat.Txt],
            overwrite: false,
            CancellationToken.None);

        Assert.EndsWith("meeting transcript 2.txt", files.TxtPath);
    }

    private static TranscriptDocument SampleDocument(string output) => new()
    {
        InputFile = Path.Combine(output, "meeting.mp3"),
        DurationSec = 10,
        CreatedAt = new DateTimeOffset(2026, 5, 7, 12, 0, 0, TimeSpan.Zero),
        AsrModelId = "model",
        AsrModelDisplayName = "Русский — быстро",
        DiarizationModelId = "diarization",
        DiarizationModelDisplayName = "Спикеры — быстро",
        Speakers = [new TranscriptSpeaker("SPEAKER_00", "Speaker 1")],
        Segments =
        [
            new TranscriptSegment(1, 1.2, 6.8, "SPEAKER_00", "Speaker 1", "Добрый день.", null)
        ],
        RawDiarizationSegments =
        [
            new DiarizationTurn(1.18, 6.82, "SPEAKER_00")
        ]
    };
}
```

- [ ] **Step 2: Implement result records**

Create:

```csharp
namespace Autorecord.Core.Transcription.Results;

public sealed record TranscriptSpeaker(string Id, string Label);

public sealed record TranscriptSegment(
    int Id,
    double Start,
    double End,
    string SpeakerId,
    string SpeakerLabel,
    string Text,
    double? Confidence);

public sealed record DiarizationTurn(double Start, double End, string SpeakerId);

public sealed record TranscriptOutputFiles(string? TxtPath, string? MarkdownPath, string? SrtPath, string? JsonPath)
{
    public IReadOnlyList<string> AllPaths => new[] { TxtPath, MarkdownPath, SrtPath, JsonPath }
        .Where(path => !string.IsNullOrWhiteSpace(path))
        .Select(path => path!)
        .ToList();
}
```

Create `TranscriptDocument.cs`:

```csharp
namespace Autorecord.Core.Transcription.Results;

public sealed record TranscriptDocument
{
    public string InputFile { get; init; } = "";
    public double DurationSec { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public string AsrModelId { get; init; } = "";
    public string AsrModelDisplayName { get; init; } = "";
    public string? DiarizationModelId { get; init; }
    public string? DiarizationModelDisplayName { get; init; }
    public IReadOnlyList<TranscriptSpeaker> Speakers { get; init; } = [];
    public IReadOnlyList<TranscriptSegment> Segments { get; init; } = [];
    public IReadOnlyList<DiarizationTurn> RawDiarizationSegments { get; init; } = [];
}
```

- [ ] **Step 3: Implement exporter**

Create `TranscriptExporter.cs` with methods for TXT, Markdown, SRT, and JSON. Use invariant timestamp formatting:

```csharp
using System.Globalization;
using System.Text;
using System.Text.Json;
using Autorecord.Core.Settings;

namespace Autorecord.Core.Transcription.Results;

public sealed class TranscriptExporter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public async Task<TranscriptOutputFiles> ExportAsync(
        TranscriptDocument document,
        string outputDirectory,
        IReadOnlyList<TranscriptOutputFormat> formats,
        bool overwrite,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(outputDirectory);
        var baseName = Path.GetFileNameWithoutExtension(document.InputFile);
        var paths = ResolveOutputPaths(outputDirectory, baseName, formats, overwrite);

        if (paths.TxtPath is not null) await WriteAtomicAsync(paths.TxtPath, BuildTxt(document), cancellationToken);
        if (paths.MarkdownPath is not null) await WriteAtomicAsync(paths.MarkdownPath, BuildMarkdown(document), cancellationToken);
        if (paths.SrtPath is not null) await WriteAtomicAsync(paths.SrtPath, BuildSrt(document), cancellationToken);
        if (paths.JsonPath is not null) await WriteAtomicAsync(paths.JsonPath, JsonSerializer.Serialize(document, JsonOptions), cancellationToken);

        return paths;
    }

    private static TranscriptOutputFiles ResolveOutputPaths(string directory, string baseName, IReadOnlyList<TranscriptOutputFormat> formats, bool overwrite)
    {
        var suffix = "";
        if (!overwrite)
        {
            for (var index = 1; ; index++)
            {
                suffix = index == 1 ? "" : $" transcript {index}";
                var anyExists = formats.Any(format => File.Exists(Path.Combine(directory, baseName + suffix + Extension(format))));
                if (!anyExists) break;
            }
        }

        return new TranscriptOutputFiles(
            formats.Contains(TranscriptOutputFormat.Txt) ? Path.Combine(directory, baseName + suffix + ".txt") : null,
            formats.Contains(TranscriptOutputFormat.Markdown) ? Path.Combine(directory, baseName + suffix + ".md") : null,
            formats.Contains(TranscriptOutputFormat.Srt) ? Path.Combine(directory, baseName + suffix + ".srt") : null,
            formats.Contains(TranscriptOutputFormat.Json) ? Path.Combine(directory, baseName + suffix + ".json") : null);
    }

    private static string Extension(TranscriptOutputFormat format) => format switch
    {
        TranscriptOutputFormat.Txt => ".txt",
        TranscriptOutputFormat.Markdown => ".md",
        TranscriptOutputFormat.Srt => ".srt",
        TranscriptOutputFormat.Json => ".json",
        _ => throw new ArgumentOutOfRangeException(nameof(format))
    };

    private static string BuildTxt(TranscriptDocument document)
    {
        var builder = new StringBuilder();
        foreach (var segment in document.Segments)
        {
            builder.AppendLine($"[{FormatShort(segment.Start)} - {FormatShort(segment.End)}] {segment.SpeakerLabel}:");
            builder.AppendLine(segment.Text);
            builder.AppendLine();
        }
        return builder.ToString();
    }

    private static string BuildMarkdown(TranscriptDocument document)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Транскрипт");
        builder.AppendLine();
        builder.AppendLine($"Файл: {Path.GetFileName(document.InputFile)}");
        builder.AppendLine($"Дата создания: {document.CreatedAt:dd.MM.yyyy HH:mm}");
        builder.AppendLine($"ASR-модель: {document.AsrModelDisplayName}");
        builder.AppendLine($"Диаризация: {document.DiarizationModelDisplayName ?? "Без разделения по спикерам"}");
        builder.AppendLine();
        builder.Append(BuildTxt(document));
        return builder.ToString();
    }

    private static string BuildSrt(TranscriptDocument document)
    {
        var builder = new StringBuilder();
        foreach (var segment in document.Segments)
        {
            builder.AppendLine(segment.Id.ToString(CultureInfo.InvariantCulture));
            builder.AppendLine($"{FormatSrt(segment.Start)} --> {FormatSrt(segment.End)}");
            builder.AppendLine($"{segment.SpeakerLabel}: {segment.Text}");
            builder.AppendLine();
        }
        return builder.ToString();
    }

    private static string FormatShort(double seconds) => TimeSpan.FromSeconds(seconds).ToString(@"hh\:mm\:ss\.fff", CultureInfo.InvariantCulture);
    private static string FormatSrt(double seconds) => TimeSpan.FromSeconds(seconds).ToString(@"hh\:mm\:ss\,fff", CultureInfo.InvariantCulture);

    private static async Task WriteAtomicAsync(string path, string content, CancellationToken cancellationToken)
    {
        var tempPath = path + ".tmp";
        await File.WriteAllTextAsync(tempPath, content, Encoding.UTF8, cancellationToken);
        File.Move(tempPath, path, overwrite: true);
    }
}
```

- [ ] **Step 4: Run tests**

```powershell
& 'C:\Users\User\.dotnet\dotnet.exe' test .\tests\Autorecord.Core.Tests\Autorecord.Core.Tests.csproj --filter TranscriptExporterTests --no-restore
```

Expected: tests pass.

- [ ] **Step 5: Commit**

```powershell
git add src/Autorecord.Core/Transcription/Results tests/Autorecord.Core.Tests/TranscriptExporterTests.cs
git commit -m "feat: export transcription results"
```

### Task 8: Add Queue With Fake Pipeline Contract

**Files:**
- Create: `src/Autorecord.Core/Transcription/Pipeline/ITranscriptionPipeline.cs`
- Create: `src/Autorecord.Core/Transcription/Pipeline/TranscriptionPipelineResult.cs`
- Create: `src/Autorecord.Core/Transcription/Jobs/TranscriptionQueue.cs`
- Test: `tests/Autorecord.Core.Tests/TranscriptionQueueTests.cs`

- [ ] **Step 1: Define pipeline contract**

```csharp
using Autorecord.Core.Transcription.Jobs;

namespace Autorecord.Core.Transcription.Pipeline;

public interface ITranscriptionPipeline
{
    Task<TranscriptionPipelineResult> RunAsync(
        TranscriptionJob job,
        IProgress<int> progress,
        CancellationToken cancellationToken);
}

public sealed record TranscriptionPipelineResult(IReadOnlyList<string> OutputFiles);
```

- [ ] **Step 2: Write queue test**

```csharp
using Autorecord.Core.Transcription.Jobs;
using Autorecord.Core.Transcription.Pipeline;

namespace Autorecord.Core.Tests;

public sealed class TranscriptionQueueTests
{
    [Fact]
    public async Task EnqueueAsyncPersistsAndCompletesJob()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "jobs.json");
        var repository = new TranscriptionJobRepository(path);
        var pipeline = new FakePipeline();
        var queue = new TranscriptionQueue(repository, pipeline, () => new DateTimeOffset(2026, 5, 7, 12, 0, 0, TimeSpan.Zero));

        var job = await queue.EnqueueAsync(
            "C:\\Records\\meeting.mp3",
            "C:\\Transcripts",
            "model",
            null,
            CancellationToken.None);

        await queue.RunNextAsync(CancellationToken.None);
        var loaded = await repository.LoadAsync(CancellationToken.None);

        Assert.Equal(job.Id, loaded[0].Id);
        Assert.Equal(TranscriptionJobStatus.Completed, loaded[0].Status);
        Assert.Equal(100, loaded[0].ProgressPercent);
    }

    private sealed class FakePipeline : ITranscriptionPipeline
    {
        public Task<TranscriptionPipelineResult> RunAsync(TranscriptionJob job, IProgress<int> progress, CancellationToken cancellationToken)
        {
            progress.Report(100);
            return Task.FromResult(new TranscriptionPipelineResult(["C:\\Transcripts\\meeting.txt"]));
        }
    }
}
```

- [ ] **Step 3: Implement queue**

```csharp
using Autorecord.Core.Transcription.Pipeline;

namespace Autorecord.Core.Transcription.Jobs;

public sealed class TranscriptionQueue
{
    private readonly TranscriptionJobRepository _repository;
    private readonly ITranscriptionPipeline _pipeline;
    private readonly Func<DateTimeOffset> _clock;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private List<TranscriptionJob> _jobs = [];

    public TranscriptionQueue(TranscriptionJobRepository repository, ITranscriptionPipeline pipeline, Func<DateTimeOffset> clock)
    {
        _repository = repository;
        _pipeline = pipeline;
        _clock = clock;
    }

    public IReadOnlyList<TranscriptionJob> Jobs => _jobs;

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        _jobs = (await _repository.LoadAsync(cancellationToken)).ToList();
    }

    public async Task<TranscriptionJob> EnqueueAsync(
        string inputFilePath,
        string outputDirectory,
        string asrModelId,
        string? diarizationModelId,
        CancellationToken cancellationToken)
    {
        var job = new TranscriptionJob
        {
            Id = Guid.NewGuid(),
            InputFilePath = inputFilePath,
            OutputDirectory = outputDirectory,
            AsrModelId = asrModelId,
            DiarizationModelId = diarizationModelId,
            Status = TranscriptionJobStatus.Pending,
            CreatedAt = _clock()
        };

        await _gate.WaitAsync(cancellationToken);
        try
        {
            _jobs.Add(job);
            await _repository.SaveAsync(_jobs, cancellationToken);
            return job;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task RunNextAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        TranscriptionJob? job;
        try
        {
            job = _jobs.FirstOrDefault(item => item.Status == TranscriptionJobStatus.Pending);
            if (job is null)
            {
                return;
            }

            Update(job.Id, item => item with { Status = TranscriptionJobStatus.Running, StartedAt = _clock(), ProgressPercent = 0 });
            await _repository.SaveAsync(_jobs, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }

        try
        {
            var progress = new Progress<int>(percent => Update(job.Id, item => item with { ProgressPercent = percent }));
            var result = await _pipeline.RunAsync(job, progress, cancellationToken);
            Update(job.Id, item => item with
            {
                Status = TranscriptionJobStatus.Completed,
                FinishedAt = _clock(),
                ProgressPercent = 100,
                OutputFiles = result.OutputFiles
            });
        }
        catch (OperationCanceledException)
        {
            Update(job.Id, item => item with { Status = TranscriptionJobStatus.Cancelled, FinishedAt = _clock(), ErrorMessage = "Cancelled." });
        }
        catch (Exception ex)
        {
            Update(job.Id, item => item with { Status = TranscriptionJobStatus.Failed, FinishedAt = _clock(), ErrorMessage = ex.Message });
        }

        await _repository.SaveAsync(_jobs, CancellationToken.None);
    }

    private void Update(Guid id, Func<TranscriptionJob, TranscriptionJob> update)
    {
        var index = _jobs.FindIndex(job => job.Id == id);
        if (index >= 0)
        {
            _jobs[index] = update(_jobs[index]);
        }
    }
}
```

- [ ] **Step 4: Run tests**

```powershell
& 'C:\Users\User\.dotnet\dotnet.exe' test .\tests\Autorecord.Core.Tests\Autorecord.Core.Tests.csproj --filter TranscriptionQueueTests --no-restore
```

Expected: tests pass.

- [ ] **Step 5: Commit**

```powershell
git add src/Autorecord.Core/Transcription/Jobs src/Autorecord.Core/Transcription/Pipeline tests/Autorecord.Core.Tests/TranscriptionQueueTests.cs
git commit -m "feat: queue transcription jobs"
```

---

## Phase D: Audio Normalization and Sherpa ASR

### Task 9: Add AudioNormalizer for App MP3/WAV Path

**Files:**
- Create: `src/Autorecord.Core/Transcription/Pipeline/AudioNormalizer.cs`
- Test: `tests/Autorecord.Core.Tests/AudioNormalizerTests.cs`

- [ ] **Step 1: Write WAV pass-through test**

```csharp
using Autorecord.Core.Transcription.Pipeline;
using NAudio.Wave;

namespace Autorecord.Core.Tests;

public sealed class AudioNormalizerTests
{
    [Fact]
    public async Task NormalizeAsyncReturnsSamePathWhenWavAlreadyMatches()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var wav = Path.Combine(dir, "input.wav");
        using (var writer = new WaveFileWriter(wav, new WaveFormat(16000, 16, 1)))
        {
            writer.Write(new byte[3200], 0, 3200);
        }

        var normalizer = new AudioNormalizer(Path.Combine(dir, "temp"));
        var result = await normalizer.NormalizeAsync(wav, keepIntermediateFiles: false, CancellationToken.None);

        Assert.Equal(wav, result.NormalizedWavPath);
        Assert.False(result.CreatedTemporaryFile);
    }
}
```

- [ ] **Step 2: Implement normalizer result and WAV matching**

```csharp
using NAudio.Wave;

namespace Autorecord.Core.Transcription.Pipeline;

public sealed record NormalizedAudio(string NormalizedWavPath, bool CreatedTemporaryFile);

public sealed class AudioNormalizer
{
    private static readonly WaveFormat TargetFormat = new(16000, 16, 1);
    private readonly string _tempRoot;

    public AudioNormalizer(string tempRoot)
    {
        _tempRoot = tempRoot;
    }

    public Task<NormalizedAudio> NormalizeAsync(string inputPath, bool keepIntermediateFiles, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (Path.GetExtension(inputPath).Equals(".wav", StringComparison.OrdinalIgnoreCase))
        {
            using var reader = new WaveFileReader(inputPath);
            if (reader.WaveFormat.SampleRate == TargetFormat.SampleRate &&
                reader.WaveFormat.Channels == TargetFormat.Channels &&
                reader.WaveFormat.BitsPerSample == TargetFormat.BitsPerSample)
            {
                return Task.FromResult(new NormalizedAudio(inputPath, CreatedTemporaryFile: false));
            }
        }

        Directory.CreateDirectory(_tempRoot);
        var outputPath = Path.Combine(_tempRoot, $"{Path.GetFileNameWithoutExtension(inputPath)}.{Guid.NewGuid():N}.normalized.wav");
        using var audioFile = new AudioFileReader(inputPath);
        var mono = audioFile.ToMono();
        using var resampler = new MediaFoundationResampler(mono, TargetFormat)
        {
            ResamplerQuality = 60
        };
        WaveFileWriter.CreateWaveFile(outputPath, resampler);
        return Task.FromResult(new NormalizedAudio(outputPath, CreatedTemporaryFile: true));
    }
}
```

- [ ] **Step 3: Add MP3 smoke test guarded by MediaFoundation**

Add a test that creates a short wav, converts to mp3 using existing `NaudioWavRecorder` helper if accessible, then normalizes MP3 to WAV. If MediaFoundation encoder is unavailable on the machine, skip with a clear exception handling pattern used in existing tests.

- [ ] **Step 4: Run tests**

```powershell
& 'C:\Users\User\.dotnet\dotnet.exe' test .\tests\Autorecord.Core.Tests\Autorecord.Core.Tests.csproj --filter AudioNormalizerTests --no-restore
```

Expected: tests pass.

- [ ] **Step 5: Commit**

```powershell
git add src/Autorecord.Core/Transcription/Pipeline/AudioNormalizer.cs tests/Autorecord.Core.Tests/AudioNormalizerTests.cs
git commit -m "feat: normalize audio for transcription"
```

### Task 10: Add SherpaOnnxTranscriptionEngine Shell and Package

**Files:**
- Modify: `src/Autorecord.Core/Autorecord.Core.csproj`
- Create: `src/Autorecord.Core/Transcription/Engines/ITranscriptionEngine.cs`
- Create: `src/Autorecord.Core/Transcription/Engines/TranscriptionEngineResult.cs`
- Create: `src/Autorecord.Core/Transcription/Engines/TranscriptionEngineSegment.cs`
- Create: `src/Autorecord.Core/Transcription/Engines/SherpaOnnxTranscriptionEngine.cs`
- Test: `tests/Autorecord.Core.Tests/SherpaOnnxTranscriptionEngineTests.cs`

- [ ] **Step 1: Add package**

```powershell
& 'C:\Users\User\.dotnet\dotnet.exe' add .\src\Autorecord.Core\Autorecord.Core.csproj package org.k2fsa.sherpa.onnx
```

Expected: NuGet restore succeeds.

- [ ] **Step 2: Define engine contracts**

```csharp
namespace Autorecord.Core.Transcription.Engines;

public interface ITranscriptionEngine
{
    Task<TranscriptionEngineResult> TranscribeAsync(
        string normalizedWavPath,
        string modelPath,
        IProgress<int> progress,
        CancellationToken cancellationToken);
}

public sealed record TranscriptionEngineResult(IReadOnlyList<TranscriptionEngineSegment> Segments);

public sealed record TranscriptionEngineSegment(double Start, double End, string Text, double? Confidence);
```

- [ ] **Step 3: Add missing model validation test**

```csharp
using Autorecord.Core.Transcription.Engines;

namespace Autorecord.Core.Tests;

public sealed class SherpaOnnxTranscriptionEngineTests
{
    [Fact]
    public async Task TranscribeAsyncThrowsWhenRequiredModelFilesAreMissing()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var engine = new SherpaOnnxTranscriptionEngine();

        var ex = await Assert.ThrowsAsync<FileNotFoundException>(() =>
            engine.TranscribeAsync("input.wav", dir, new Progress<int>(), CancellationToken.None));

        Assert.Contains("tokens.txt", ex.Message);
    }
}
```

- [ ] **Step 4: Implement shell validation**

```csharp
namespace Autorecord.Core.Transcription.Engines;

public sealed class SherpaOnnxTranscriptionEngine : ITranscriptionEngine
{
    public Task<TranscriptionEngineResult> TranscribeAsync(
        string normalizedWavPath,
        string modelPath,
        IProgress<int> progress,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        RequireFile(modelPath, "tokens.txt");
        RequireFile(modelPath, "model.int8.onnx");

        progress.Report(0);
        throw new NotSupportedException("Sherpa runtime wiring is added in the next step after package API verification.");
    }

    private static void RequireFile(string modelPath, string fileName)
    {
        var path = Path.Combine(modelPath, fileName);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Required sherpa model file is missing: {fileName}", path);
        }
    }
}
```

- [ ] **Step 5: Replace NotSupportedException with sherpa decode**

Use the installed `org.k2fsa.sherpa.onnx` package and the official C# offline decode example. Configure a non-streaming NeMo CTC model with:

- model: `Path.Combine(modelPath, "model.int8.onnx")`
- tokens: `Path.Combine(modelPath, "tokens.txt")`
- sample rate: 16000
- feature dim and decoding parameters from sherpa defaults for NeMo CTC examples.

Return at least one `TranscriptionEngineSegment`. If the sherpa C# API does not provide timestamps for this model, return one segment spanning the full audio duration. Progress must report 100 after decode.

- [ ] **Step 6: Add real model manual check**

After Task 5 can download the model, run a manual check with a short `.wav`:

```powershell
& 'C:\Users\User\.dotnet\dotnet.exe' test .\tests\Autorecord.Core.Tests\Autorecord.Core.Tests.csproj --filter SherpaOnnxTranscriptionEngineTests --no-restore
```

Expected: validation tests pass. Real decode is verified manually from the GUI after model download.

- [ ] **Step 7: Commit**

```powershell
git add src/Autorecord.Core/Autorecord.Core.csproj src/Autorecord.Core/Transcription/Engines tests/Autorecord.Core.Tests/SherpaOnnxTranscriptionEngineTests.cs
git commit -m "feat: add sherpa transcription engine"
```

---

## Phase E: Diarization and Transcript Assembly

### Task 11: Add Diarization Engine Contract and Segment Cleanup

**Files:**
- Create: `src/Autorecord.Core/Transcription/Diarization/IDiarizationEngine.cs`
- Create: `src/Autorecord.Core/Transcription/Diarization/DiarizationEngine.cs`
- Test: `tests/Autorecord.Core.Tests/DiarizationEngineTests.cs`

- [ ] **Step 1: Write cleanup test**

```csharp
using Autorecord.Core.Transcription.Diarization;
using Autorecord.Core.Transcription.Results;

namespace Autorecord.Core.Tests;

public sealed class DiarizationEngineTests
{
    [Fact]
    public void NormalizeTurnsMergesSameSpeakerAndDropsShortTurns()
    {
        var turns = new[]
        {
            new DiarizationTurn(0.0, 1.0, "SPEAKER_00"),
            new DiarizationTurn(1.5, 2.0, "SPEAKER_00"),
            new DiarizationTurn(2.1, 2.2, "SPEAKER_01")
        };

        var result = DiarizationEngine.NormalizeTurns(turns);

        Assert.Single(result);
        Assert.Equal(0.0, result[0].Start);
        Assert.Equal(2.0, result[0].End);
        Assert.Equal("SPEAKER_00", result[0].SpeakerId);
    }
}
```

- [ ] **Step 2: Implement contract and cleanup**

```csharp
using Autorecord.Core.Transcription.Results;

namespace Autorecord.Core.Transcription.Diarization;

public interface IDiarizationEngine
{
    Task<IReadOnlyList<DiarizationTurn>> DiarizeAsync(
        string normalizedWavPath,
        string modelPath,
        int? numSpeakers,
        double? clusterThreshold,
        IProgress<int> progress,
        CancellationToken cancellationToken);
}

public sealed class DiarizationEngine : IDiarizationEngine
{
    public Task<IReadOnlyList<DiarizationTurn>> DiarizeAsync(
        string normalizedWavPath,
        string modelPath,
        int? numSpeakers,
        double? clusterThreshold,
        IProgress<int> progress,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        RequireDiarizationFiles(modelPath);
        progress.Report(0);
        throw new NotSupportedException("Sherpa diarization runtime wiring is added after model validation and API verification.");
    }

    public static IReadOnlyList<DiarizationTurn> NormalizeTurns(IEnumerable<DiarizationTurn> turns)
    {
        var filtered = turns
            .Where(turn => turn.End - turn.Start >= 0.25)
            .OrderBy(turn => turn.Start)
            .ToList();

        var result = new List<DiarizationTurn>();
        foreach (var turn in filtered)
        {
            var previous = result.LastOrDefault();
            if (previous is not null &&
                previous.SpeakerId == turn.SpeakerId &&
                turn.Start - previous.End <= 0.7)
            {
                result[^1] = previous with { End = Math.Max(previous.End, turn.End) };
            }
            else
            {
                result.Add(turn);
            }
        }

        return result;
    }

    private static void RequireDiarizationFiles(string modelPath)
    {
        if (!File.Exists(Path.Combine(modelPath, "model.onnx")) &&
            !File.Exists(Path.Combine(modelPath, "model.int8.onnx")))
        {
            throw new FileNotFoundException("Required diarization segmentation model is missing.");
        }
    }
}
```

- [ ] **Step 3: Wire sherpa diarization API**

Use sherpa-onnx C# diarization examples. Return normalized `DiarizationTurn` values. Support:

- `numSpeakers == null` for auto;
- `1..6` fixed speaker count;
- optional `clusterThreshold`;
- cancellation before and after native call;
- progress `0` before call and `100` after call.

- [ ] **Step 4: Run tests**

```powershell
& 'C:\Users\User\.dotnet\dotnet.exe' test .\tests\Autorecord.Core.Tests\Autorecord.Core.Tests.csproj --filter DiarizationEngineTests --no-restore
```

Expected: cleanup tests pass; runtime decode is manually verified after model download.

- [ ] **Step 5: Commit**

```powershell
git add src/Autorecord.Core/Transcription/Diarization tests/Autorecord.Core.Tests/DiarizationEngineTests.cs
git commit -m "feat: add diarization engine"
```

### Task 12: Add TranscriptAssembler

**Files:**
- Create: `src/Autorecord.Core/Transcription/Pipeline/TranscriptAssembler.cs`
- Test: `tests/Autorecord.Core.Tests/TranscriptAssemblerTests.cs`

- [ ] **Step 1: Write assignment and merge tests**

```csharp
using Autorecord.Core.Transcription.Engines;
using Autorecord.Core.Transcription.Pipeline;
using Autorecord.Core.Transcription.Results;

namespace Autorecord.Core.Tests;

public sealed class TranscriptAssemblerTests
{
    [Fact]
    public void AssembleAssignsSegmentsToOverlappingSpeakerTurns()
    {
        var asr = new[]
        {
            new TranscriptionEngineSegment(1.0, 3.0, "Добрый день.", null),
            new TranscriptionEngineSegment(4.0, 5.0, "Да.", null)
        };
        var turns = new[]
        {
            new DiarizationTurn(0.5, 3.5, "SPEAKER_00"),
            new DiarizationTurn(3.8, 5.5, "SPEAKER_01")
        };

        var segments = TranscriptAssembler.Assemble(asr, turns);

        Assert.Equal("Speaker 1", segments[0].SpeakerLabel);
        Assert.Equal("Speaker 2", segments[1].SpeakerLabel);
    }
}
```

- [ ] **Step 2: Implement assembler**

```csharp
using Autorecord.Core.Transcription.Engines;
using Autorecord.Core.Transcription.Results;

namespace Autorecord.Core.Transcription.Pipeline;

public static class TranscriptAssembler
{
    public static IReadOnlyList<TranscriptSegment> Assemble(
        IReadOnlyList<TranscriptionEngineSegment> asrSegments,
        IReadOnlyList<DiarizationTurn> turns)
    {
        var speakerLabels = turns
            .Select(turn => turn.SpeakerId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select((id, index) => new { id, label = $"Speaker {index + 1}" })
            .ToDictionary(item => item.id, item => item.label, StringComparer.OrdinalIgnoreCase);

        var result = new List<TranscriptSegment>();
        foreach (var segment in asrSegments.OrderBy(segment => segment.Start))
        {
            var speakerId = FindBestSpeaker(segment, turns) ?? "SPEAKER_00";
            if (!speakerLabels.TryGetValue(speakerId, out var label))
            {
                label = "Speaker 1";
            }

            result.Add(new TranscriptSegment(result.Count + 1, segment.Start, segment.End, speakerId, label, segment.Text, segment.Confidence));
        }

        return MergeAdjacent(result);
    }

    private static string? FindBestSpeaker(TranscriptionEngineSegment segment, IReadOnlyList<DiarizationTurn> turns)
    {
        return turns
            .Select(turn => new { turn, overlap = Math.Min(segment.End, turn.End) - Math.Max(segment.Start, turn.Start) })
            .Where(item => item.overlap > 0)
            .OrderByDescending(item => item.overlap)
            .Select(item => item.turn.SpeakerId)
            .FirstOrDefault();
    }

    private static IReadOnlyList<TranscriptSegment> MergeAdjacent(IReadOnlyList<TranscriptSegment> segments)
    {
        var result = new List<TranscriptSegment>();
        foreach (var segment in segments)
        {
            var previous = result.LastOrDefault();
            if (previous is not null &&
                previous.SpeakerId == segment.SpeakerId &&
                segment.Start - previous.End <= 1.0 &&
                previous.Text.Length + segment.Text.Length <= 600)
            {
                result[^1] = previous with { End = segment.End, Text = previous.Text + " " + segment.Text };
            }
            else
            {
                result.Add(segment with { Id = result.Count + 1 });
            }
        }

        return result.Select((segment, index) => segment with { Id = index + 1 }).ToList();
    }
}
```

- [ ] **Step 3: Run tests and commit**

```powershell
& 'C:\Users\User\.dotnet\dotnet.exe' test .\tests\Autorecord.Core.Tests\Autorecord.Core.Tests.csproj --filter TranscriptAssemblerTests --no-restore
git add src/Autorecord.Core/Transcription/Pipeline/TranscriptAssembler.cs tests/Autorecord.Core.Tests/TranscriptAssemblerTests.cs
git commit -m "feat: assemble speaker transcripts"
```

---

## Phase F: GigaAM v3 Worker Contract

### Task 13: Add GigaAM Worker Client With Fake Worker Test

**Files:**
- Create: `src/Autorecord.Core/Transcription/Engines/GigaAmWorkerClient.cs`
- Create: `src/Autorecord.Core/Transcription/Engines/GigaAmV3TranscriptionEngine.cs`
- Test: `tests/Autorecord.Core.Tests/GigaAmWorkerClientTests.cs`

- [ ] **Step 1: Write worker client JSON test**

```csharp
using Autorecord.Core.Transcription.Engines;

namespace Autorecord.Core.Tests;

public sealed class GigaAmWorkerClientTests
{
    [Fact]
    public void ParseResultReadsSegments()
    {
        var json = """
        {
          "segments": [
            { "start": 1.2, "end": 3.4, "text": "Привет", "confidence": null }
          ]
        }
        """;

        var result = GigaAmWorkerClient.ParseResult(json);

        Assert.Single(result.Segments);
        Assert.Equal("Привет", result.Segments[0].Text);
    }
}
```

- [ ] **Step 2: Implement worker client parser**

```csharp
using System.Text.Json;

namespace Autorecord.Core.Transcription.Engines;

public sealed class GigaAmWorkerClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static TranscriptionEngineResult ParseResult(string json)
    {
        var dto = JsonSerializer.Deserialize<WorkerResultDto>(json, JsonOptions)
            ?? new WorkerResultDto();
        return new TranscriptionEngineResult(dto.Segments
            .Select(segment => new TranscriptionEngineSegment(segment.Start, segment.End, segment.Text, segment.Confidence))
            .ToList());
    }

    private sealed record WorkerResultDto
    {
        public IReadOnlyList<WorkerSegmentDto> Segments { get; init; } = [];
    }

    private sealed record WorkerSegmentDto
    {
        public double Start { get; init; }
        public double End { get; init; }
        public string Text { get; init; } = "";
        public double? Confidence { get; init; }
    }
}
```

- [ ] **Step 3: Implement GigaAM engine shell**

Create `GigaAmV3TranscriptionEngine.cs`:

```csharp
namespace Autorecord.Core.Transcription.Engines;

public sealed class GigaAmV3TranscriptionEngine : ITranscriptionEngine
{
    private readonly string _workerPath;
    private readonly GigaAmWorkerClient _client;

    public GigaAmV3TranscriptionEngine(string workerPath, GigaAmWorkerClient client)
    {
        _workerPath = workerPath;
        _client = client;
    }

    public Task<TranscriptionEngineResult> TranscribeAsync(
        string normalizedWavPath,
        string modelPath,
        IProgress<int> progress,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!File.Exists(_workerPath))
        {
            throw new FileNotFoundException("GigaAM worker is not installed.", _workerPath);
        }

        if (!Directory.Exists(modelPath))
        {
            throw new DirectoryNotFoundException($"GigaAM model folder is not installed: {modelPath}");
        }

        throw new NotSupportedException("GigaAM worker process execution is added after worker artifact packaging.");
    }
}
```

- [ ] **Step 4: Add process execution**

Implement `GigaAmWorkerClient.RunAsync` using `System.Diagnostics.ProcessStartInfo`:

- `UseShellExecute = false`
- `CreateNoWindow = true`
- arguments include `--input`, `--model`, `--output-json`
- read JSON output from the output file
- support cancellation by killing the process tree
- map non-zero exit code to `InvalidOperationException`

- [ ] **Step 5: Run tests and commit**

```powershell
& 'C:\Users\User\.dotnet\dotnet.exe' test .\tests\Autorecord.Core.Tests\Autorecord.Core.Tests.csproj --filter GigaAmWorkerClientTests --no-restore
git add src/Autorecord.Core/Transcription/Engines/GigaAmWorkerClient.cs src/Autorecord.Core/Transcription/Engines/GigaAmV3TranscriptionEngine.cs tests/Autorecord.Core.Tests/GigaAmWorkerClientTests.cs
git commit -m "feat: add gigaam worker contract"
```

---

## Phase G: Pipeline, WPF Wiring, and Recording Integration

### Task 14: Add TranscriptionPipeline

**Files:**
- Create: `src/Autorecord.Core/Transcription/Pipeline/TranscriptionPipeline.cs`
- Test: `tests/Autorecord.Core.Tests/TranscriptionPipelineTests.cs`

- [ ] **Step 1: Write pipeline export test with fake engine**

Create a fake `ITranscriptionEngine` returning one segment and a fake `IDiarizationEngine` returning one speaker turn. Assert that `RunAsync` creates all selected output files in the job output folder.

- [ ] **Step 2: Implement pipeline**

Pipeline constructor dependencies:

```csharp
public TranscriptionPipeline(
    ModelCatalog catalog,
    ModelManager modelManager,
    AudioNormalizer audioNormalizer,
    IReadOnlyDictionary<string, ITranscriptionEngine> asrEngines,
    IDiarizationEngine diarizationEngine,
    TranscriptExporter exporter,
    TranscriptionSettings settings)
```

Run behavior:

- get ASR catalog entry by job model id;
- if ASR model not installed, throw `ModelNotInstalled`;
- normalize input audio;
- if diarization enabled and job has diarization model id, run diarization;
- run ASR;
- assemble segments;
- export selected formats;
- delete temporary normalized wav after success when it was created and `KeepIntermediateFiles` is false.

- [ ] **Step 3: Run tests and commit**

```powershell
& 'C:\Users\User\.dotnet\dotnet.exe' test .\tests\Autorecord.Core.Tests\Autorecord.Core.Tests.csproj --filter TranscriptionPipelineTests --no-restore
git add src/Autorecord.Core/Transcription/Pipeline tests/Autorecord.Core.Tests/TranscriptionPipelineTests.cs
git commit -m "feat: run transcription pipeline"
```

### Task 15: Wire WPF Model and Queue Actions

**Files:**
- Modify: `src/Autorecord.App/App.xaml.cs`
- Modify: `src/Autorecord.App/MainWindow.xaml.cs`
- Create: `src/Autorecord.App/Notifications/TranscriptionNotificationService.cs`

- [ ] **Step 1: Add app paths**

Add helpers in `App.xaml.cs`:

```csharp
private static string GetAppDataPath(params string[] parts)
{
    return Path.Combine(
        [Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Autorecord", .. parts]);
}
```

- [ ] **Step 2: Initialize services**

In `OnStartup`, initialize:

- `ModelCatalog.LoadAsync(Path.Combine(AppContext.BaseDirectory, "models", "catalog.json"), token)` with fallback to repo-relative path during development;
- `ModelManager(GetAppDataPath("Models"))`;
- `ModelDownloadService(_httpClient, GetAppDataPath("Temp", "Downloads"))`;
- `TranscriptionJobRepository(GetAppDataPath("transcription-jobs.json"))`;
- `TranscriptionQueue(...)`.

- [ ] **Step 3: Wire model buttons**

Button handlers:

- download selected model through `ModelDownloadService`;
- delete selected model through `ModelManager.DeleteAsync`;
- validate selected model through `ModelManager.GetStatusAsync`;
- open models folder with `Process.Start(new ProcessStartInfo(path) { UseShellExecute = true })`.

- [ ] **Step 4: Wire manual file pick**

Use `Microsoft.Win32.OpenFileDialog` with filter:

```csharp
"Audio and video|*.wav;*.mp3;*.m4a;*.flac;*.ogg;*.mp4;*.mkv|All files|*.*"
```

Resolve output directory from transcription settings and enqueue.

- [ ] **Step 5: Build and smoke run**

```powershell
& 'C:\Users\User\.dotnet\dotnet.exe' build .\Autorecord.sln --no-restore
```

Expected: build succeeds.

- [ ] **Step 6: Commit**

```powershell
git add src/Autorecord.App
git commit -m "feat: wire transcription gui actions"
```

### Task 16: Connect RecordingSaved to TranscriptionQueue

**Files:**
- Modify: `src/Autorecord.App/App.xaml.cs`

- [ ] **Step 1: Add enqueue helper**

In `App.xaml.cs`, add:

```csharp
private async Task EnqueueRecordingForTranscriptionAsync(RecordingSession session, CancellationToken cancellationToken)
{
    if (!_settings.Transcription.AutoTranscribeAfterRecording || _transcriptionQueue is null)
    {
        return;
    }

    var outputDirectory = ResolveTranscriptOutputDirectory(session.OutputPath, _settings.Transcription);
    var diarizationModelId = _settings.Transcription.EnableDiarization
        ? _settings.Transcription.SelectedDiarizationModelId
        : null;

    await _transcriptionQueue.EnqueueAsync(
        session.OutputPath,
        outputDirectory,
        _settings.Transcription.SelectedAsrModelId,
        diarizationModelId,
        cancellationToken);
}

private static string ResolveTranscriptOutputDirectory(string inputFilePath, TranscriptionSettings settings)
{
    return settings.OutputFolderMode == TranscriptOutputFolderMode.CustomFolder
        ? settings.CustomOutputFolder ?? throw new InvalidOperationException("Transcript output folder is not configured.")
        : Path.GetDirectoryName(inputFilePath) ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
}
```

- [ ] **Step 2: Call helper from RecordingSaved**

In `RecordingCoordinator_RecordingSaved`, after existing notification/status handling:

```csharp
_ = EnqueueRecordingForTranscriptionAsync(session, _shutdown.Token);
```

- [ ] **Step 3: Build**

```powershell
& 'C:\Users\User\.dotnet\dotnet.exe' build .\Autorecord.sln --no-restore
```

Expected: build succeeds.

- [ ] **Step 4: Commit**

```powershell
git add src/Autorecord.App/App.xaml.cs
git commit -m "feat: enqueue recordings for transcription"
```

---

## Verification Pass

### Task 17: Automated Verification

**Files:**
- Modify only files needed to fix verification failures.

- [ ] **Step 1: Run full tests**

```powershell
& 'C:\Users\User\.dotnet\dotnet.exe' test .\Autorecord.sln --no-restore
```

Expected: all tests pass.

- [ ] **Step 2: Run release build**

```powershell
& 'C:\Users\User\.dotnet\dotnet.exe' build .\Autorecord.sln -c Release --no-restore
```

Expected: 0 warnings, 0 errors.

- [ ] **Step 3: Publish**

```powershell
.\scripts\publish.ps1
```

Expected: publish succeeds and writes `artifacts\publish\Autorecord\Autorecord.App.exe`.

- [ ] **Step 4: Smoke start**

Start published exe with `--minimized`, wait 5 seconds, verify process exists, then stop it.

- [ ] **Step 5: Commit fixes**

```powershell
git status --short
git add <only verification fix files>
git commit -m "fix: complete transcription verification"
```

### Task 18: Manual Acceptance Checks

Run these manually on Windows:

- Open the app and verify the existing recording tab still loads.
- Verify old recording settings load.
- Open `Транскрибация`.
- Verify ASR models are loaded from `models/catalog.json`.
- Click `Скачать модель` for `Русский — быстро`.
- Confirm progress bar moves and model becomes installed.
- Disconnect internet.
- Record or select an existing app-created `.mp3`.
- Queue transcription.
- Confirm UI remains responsive.
- Confirm `.txt`, `.md`, `.srt`, `.json` appear in the configured transcript folder.
- Enable diarization after diarization model install.
- Confirm `.txt` and `.md` include speaker labels.
- Confirm `.json` contains `rawDiarizationSegments`.
- Restart app and confirm history persists.
- Delete installed model from GUI and confirm status changes.

---

## Self-Review

- Spec coverage:
  - Local-only transcription: Tasks 10-16.
  - Model catalog and GUI model list: Tasks 2-3.
  - Model download through GUI: Tasks 5 and 15.
  - Separate recording and transcript folders: Tasks 1, 14, 16.
  - Queue/history persistence: Tasks 6 and 8.
  - TXT/MD/SRT/JSON export: Task 7.
  - Sherpa ASR: Task 10.
  - Diarization: Tasks 11-12.
  - GigaAM worker contract: Task 13.
  - Auto enqueue after recording: Task 16.
  - Manual file selection: Task 15.
  - No cloud APIs or telemetry: architecture boundary and service wiring in Tasks 5, 10, 13, 14, 15.
- Known phased risk:
  - GigaAM v3 packaging may require a separate worker artifact build, but Task 13 establishes the app contract and hidden worker execution path.
  - Non-WAV/MP3 formats depend on the final audio decoder path; app-created `.mp3` and normalized `.wav` are required for v2 verification.
- Type consistency:
  - `TranscriptionSettings`, `TranscriptionJob`, `TranscriptDocument`, `ITranscriptionEngine`, `IDiarizationEngine`, and `TranscriptionPipeline` names are consistent across tasks.

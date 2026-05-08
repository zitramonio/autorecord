using System.Text.Json;
using Autorecord.Core.Settings;
using Autorecord.Core.Transcription.Diarization;
using Autorecord.Core.Transcription.Engines;
using Autorecord.Core.Transcription.Jobs;
using Autorecord.Core.Transcription.Models;
using Autorecord.Core.Transcription.Pipeline;
using Autorecord.Core.Transcription.Results;
using NAudio.Wave;

namespace Autorecord.Core.Tests;

public sealed class TranscriptionPipelineTests
{
    [Fact]
    public async Task RunAsyncExportsSelectedFormatsToJobOutputFolder()
    {
        var root = CreateTempRoot();
        try
        {
            var inputPath = Path.Combine(root, "meeting.wav");
            var outputDirectory = Path.Combine(root, "out");
            CreateSilentWav(inputPath, new WaveFormat(16_000, 16, 1));
            var catalog = await CreateCatalogAsync(root);
            InstallModel(root, "asr-fast");
            InstallModel(root, "diarization-fast");
            var asr = new FakeTranscriptionEngine();
            var diarization = new FakeDiarizationEngine();
            var pipeline = CreatePipeline(
                root,
                catalog,
                new Dictionary<string, ITranscriptionEngine> { ["fake-asr"] = asr },
                diarization,
                new TranscriptionSettings
                {
                    EnableDiarization = true,
                    OutputFormats = [TranscriptOutputFormat.Txt, TranscriptOutputFormat.Json],
                    OverwriteExistingTranscripts = true
                });

            var result = await pipeline.RunAsync(
                CreateJob(inputPath, outputDirectory, "asr-fast", "diarization-fast"),
                new Progress<int>(),
                CancellationToken.None);

            Assert.Equal(
                [
                    Path.Combine(outputDirectory, "meeting.txt"),
                    Path.Combine(outputDirectory, "meeting.json")
                ],
                result.OutputFiles);
            Assert.All(result.OutputFiles, path => Assert.True(File.Exists(path), path));
            Assert.Equal(1, asr.CallCount);
            Assert.Equal(1, diarization.CallCount);

            await using var stream = File.OpenRead(Path.Combine(outputDirectory, "meeting.json"));
            using var json = await JsonDocument.ParseAsync(stream);
            Assert.Equal("Fake ASR", json.RootElement.GetProperty("asrModelDisplayName").GetString());
            Assert.Equal("Fake Diarization", json.RootElement.GetProperty("diarizationModelDisplayName").GetString());
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsyncThrowsModelNotInstalledBeforeAsrRuns()
    {
        var root = CreateTempRoot();
        try
        {
            var inputPath = Path.Combine(root, "meeting.wav");
            CreateSilentWav(inputPath, new WaveFormat(16_000, 16, 1));
            var catalog = await CreateCatalogAsync(root);
            var asr = new FakeTranscriptionEngine();
            var pipeline = CreatePipeline(
                root,
                catalog,
                new Dictionary<string, ITranscriptionEngine> { ["fake-asr"] = asr },
                new FakeDiarizationEngine(),
                new TranscriptionSettings { OutputFormats = [TranscriptOutputFormat.Txt] });

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => pipeline.RunAsync(
                    CreateJob(inputPath, Path.Combine(root, "out"), "asr-fast", null),
                    new Progress<int>(),
                    CancellationToken.None));

            Assert.Contains("ModelNotInstalled", exception.Message);
            Assert.Contains("asr-fast", exception.Message);
            Assert.Equal(0, asr.CallCount);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsyncThrowsWhenDiarizationModelMissingBeforeNormalizationOrEnginesRun()
    {
        var root = CreateTempRoot();
        try
        {
            var inputPath = Path.Combine(root, "meeting.wav");
            var normalizedRoot = Path.Combine(root, "normalized");
            CreateSilentWav(inputPath, new WaveFormat(48_000, 16, 2));
            var catalog = await CreateCatalogAsync(root);
            InstallModel(root, "asr-fast");
            var asr = new FakeTranscriptionEngine();
            var diarization = new FakeDiarizationEngine();
            var pipeline = CreatePipeline(
                root,
                catalog,
                new Dictionary<string, ITranscriptionEngine> { ["fake-asr"] = asr },
                diarization,
                new TranscriptionSettings
                {
                    EnableDiarization = true,
                    OutputFormats = [TranscriptOutputFormat.Txt],
                    KeepIntermediateFiles = false
                });

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => pipeline.RunAsync(
                    CreateJob(inputPath, Path.Combine(root, "out"), "asr-fast", "diarization-fast"),
                    new Progress<int>(),
                    CancellationToken.None));

            Assert.Contains("ModelNotInstalled", exception.Message);
            Assert.Contains("diarization-fast", exception.Message);
            Assert.Equal(0, asr.CallCount);
            Assert.Equal(0, diarization.CallCount);
            Assert.False(Directory.Exists(normalizedRoot));
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsyncMapsEngineProgressToMonotonicPipelineProgress()
    {
        var root = CreateTempRoot();
        try
        {
            var inputPath = Path.Combine(root, "meeting.wav");
            CreateSilentWav(inputPath, new WaveFormat(16_000, 16, 1));
            var catalog = await CreateCatalogAsync(root);
            InstallModel(root, "asr-fast");
            InstallModel(root, "diarization-fast");
            var progress = new CollectingProgress();
            var pipeline = CreatePipeline(
                root,
                catalog,
                new Dictionary<string, ITranscriptionEngine>
                {
                    ["fake-asr"] = new FakeTranscriptionEngine { ReportProgressEdges = true }
                },
                new FakeDiarizationEngine { ReportProgressEdges = true },
                new TranscriptionSettings
                {
                    EnableDiarization = true,
                    OutputFormats = [TranscriptOutputFormat.Txt],
                    OverwriteExistingTranscripts = true
                });

            await pipeline.RunAsync(
                CreateJob(inputPath, Path.Combine(root, "out"), "asr-fast", "diarization-fast"),
                progress,
                CancellationToken.None);

            Assert.NotEmpty(progress.Values);
            Assert.Equal(100, progress.Values[^1]);
            Assert.All(progress.Values.Zip(progress.Values.Skip(1)), pair => Assert.True(pair.First <= pair.Second));
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsyncDeletesTemporaryNormalizedWavAfterSuccessWhenConfigured()
    {
        var root = CreateTempRoot();
        try
        {
            var inputPath = Path.Combine(root, "meeting.wav");
            CreateSilentWav(inputPath, new WaveFormat(48_000, 16, 2));
            var catalog = await CreateCatalogAsync(root);
            InstallModel(root, "asr-fast");
            var asr = new FakeTranscriptionEngine();
            var pipeline = CreatePipeline(
                root,
                catalog,
                new Dictionary<string, ITranscriptionEngine> { ["fake-asr"] = asr },
                new FakeDiarizationEngine(),
                new TranscriptionSettings
                {
                    OutputFormats = [TranscriptOutputFormat.Txt],
                    KeepIntermediateFiles = false,
                    OverwriteExistingTranscripts = true
                });

            await pipeline.RunAsync(
                CreateJob(inputPath, Path.Combine(root, "out"), "asr-fast", null),
                new Progress<int>(),
                CancellationToken.None);

            Assert.NotNull(asr.LastNormalizedWavPath);
            Assert.NotEqual(inputPath, asr.LastNormalizedWavPath);
            Assert.False(File.Exists(asr.LastNormalizedWavPath));
            Assert.True(File.Exists(inputPath));
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsyncKeepsTemporaryNormalizedWavWhenConfigured()
    {
        var root = CreateTempRoot();
        try
        {
            var inputPath = Path.Combine(root, "meeting.wav");
            CreateSilentWav(inputPath, new WaveFormat(48_000, 16, 2));
            var catalog = await CreateCatalogAsync(root);
            InstallModel(root, "asr-fast");
            var asr = new FakeTranscriptionEngine();
            var pipeline = CreatePipeline(
                root,
                catalog,
                new Dictionary<string, ITranscriptionEngine> { ["fake-asr"] = asr },
                new FakeDiarizationEngine(),
                new TranscriptionSettings
                {
                    OutputFormats = [TranscriptOutputFormat.Txt],
                    KeepIntermediateFiles = true,
                    OverwriteExistingTranscripts = true
                });

            await pipeline.RunAsync(
                CreateJob(inputPath, Path.Combine(root, "out"), "asr-fast", null),
                new Progress<int>(),
                CancellationToken.None);

            Assert.NotNull(asr.LastNormalizedWavPath);
            Assert.NotEqual(inputPath, asr.LastNormalizedWavPath);
            Assert.True(File.Exists(asr.LastNormalizedWavPath));
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsyncKeepsTemporaryNormalizedWavWhenAsrFails()
    {
        var root = CreateTempRoot();
        try
        {
            var inputPath = Path.Combine(root, "meeting.wav");
            CreateSilentWav(inputPath, new WaveFormat(48_000, 16, 2));
            var catalog = await CreateCatalogAsync(root);
            InstallModel(root, "asr-fast");
            var asr = new FakeTranscriptionEngine { Exception = new InvalidOperationException("ASR failed") };
            var pipeline = CreatePipeline(
                root,
                catalog,
                new Dictionary<string, ITranscriptionEngine> { ["fake-asr"] = asr },
                new FakeDiarizationEngine(),
                new TranscriptionSettings
                {
                    OutputFormats = [TranscriptOutputFormat.Txt],
                    KeepIntermediateFiles = false,
                    OverwriteExistingTranscripts = true
                });

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => pipeline.RunAsync(
                    CreateJob(inputPath, Path.Combine(root, "out"), "asr-fast", null),
                    new Progress<int>(),
                    CancellationToken.None));

            Assert.NotNull(asr.LastNormalizedWavPath);
            Assert.True(File.Exists(asr.LastNormalizedWavPath));
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsyncSkipsDiarizationWhenDisabled()
    {
        var root = CreateTempRoot();
        try
        {
            var inputPath = Path.Combine(root, "meeting.wav");
            var outputDirectory = Path.Combine(root, "out");
            CreateSilentWav(inputPath, new WaveFormat(16_000, 16, 1));
            var catalog = await CreateCatalogAsync(root);
            InstallModel(root, "asr-fast");
            var diarization = new FakeDiarizationEngine();
            var pipeline = CreatePipeline(
                root,
                catalog,
                new Dictionary<string, ITranscriptionEngine> { ["fake-asr"] = new FakeTranscriptionEngine() },
                diarization,
                new TranscriptionSettings
                {
                    EnableDiarization = false,
                    OutputFormats = [TranscriptOutputFormat.Json],
                    OverwriteExistingTranscripts = true
                });

            await pipeline.RunAsync(
                CreateJob(inputPath, outputDirectory, "asr-fast", "diarization-fast"),
                new Progress<int>(),
                CancellationToken.None);

            Assert.Equal(0, diarization.CallCount);
            var json = await File.ReadAllTextAsync(Path.Combine(outputDirectory, "meeting.json"));
            Assert.Contains("\"speakerLabel\": \"Speaker 1\"", json);
            Assert.Contains("\"rawDiarizationSegments\": []", json);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    private static TranscriptionPipeline CreatePipeline(
        string root,
        ModelCatalog catalog,
        IReadOnlyDictionary<string, ITranscriptionEngine> asrEngines,
        IDiarizationEngine diarizationEngine,
        TranscriptionSettings settings)
    {
        return new TranscriptionPipeline(
            catalog,
            new ModelManager(Path.Combine(root, "models")),
            new AudioNormalizer(Path.Combine(root, "normalized")),
            asrEngines,
            diarizationEngine,
            new TranscriptExporter(),
            settings);
    }

    private static TranscriptionJob CreateJob(
        string inputPath,
        string outputDirectory,
        string asrModelId,
        string? diarizationModelId)
    {
        return new TranscriptionJob
        {
            Id = Guid.NewGuid(),
            InputFilePath = inputPath,
            OutputDirectory = outputDirectory,
            AsrModelId = asrModelId,
            DiarizationModelId = diarizationModelId,
            CreatedAt = DateTimeOffset.UtcNow
        };
    }

    private static async Task<ModelCatalog> CreateCatalogAsync(string root)
    {
        var catalogPath = Path.Combine(root, "catalog.json");
        await File.WriteAllTextAsync(
            catalogPath,
            """
            {
              "models": [
                {
                  "id": "asr-fast",
                  "displayName": "Fake ASR",
                  "type": "asr",
                  "engine": "fake-asr",
                  "download": { "url": "https://example.com/asr.zip" },
                  "install": { "targetFolder": "asr-fast", "requiredFiles": [ "model.bin" ] },
                  "runtime": { "sampleRate": 16000, "channels": 1, "device": "cpu" }
                },
                {
                  "id": "diarization-fast",
                  "displayName": "Fake Diarization",
                  "type": "diarization",
                  "engine": "fake-diarization",
                  "download": { "url": "https://example.com/diarization.zip" },
                  "install": { "targetFolder": "diarization-fast", "requiredFiles": [ "model.bin" ] },
                  "runtime": { "sampleRate": 16000, "channels": 1, "device": "cpu" }
                }
              ]
            }
            """);

        return await ModelCatalog.LoadAsync(catalogPath, CancellationToken.None);
    }

    private static void InstallModel(string root, string modelId)
    {
        var modelPath = Path.Combine(root, "models", modelId);
        Directory.CreateDirectory(modelPath);
        File.WriteAllText(Path.Combine(modelPath, "model.bin"), "");
    }

    private static void CreateSilentWav(string path, WaveFormat waveFormat)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
        using var writer = new WaveFileWriter(path, waveFormat);
        writer.Write(new byte[waveFormat.AverageBytesPerSecond / 10], 0, waveFormat.AverageBytesPerSecond / 10);
    }

    private static string CreateTempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void DeleteDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    private sealed class FakeTranscriptionEngine : ITranscriptionEngine
    {
        public int CallCount { get; private set; }
        public string? LastNormalizedWavPath { get; private set; }
        public Exception? Exception { get; init; }
        public bool ReportProgressEdges { get; init; }

        public Task<TranscriptionEngineResult> TranscribeAsync(
            string normalizedWavPath,
            string modelPath,
            IProgress<int> progress,
            CancellationToken cancellationToken)
        {
            CallCount++;
            LastNormalizedWavPath = normalizedWavPath;
            if (Exception is not null)
            {
                throw Exception;
            }

            if (ReportProgressEdges)
            {
                progress.Report(0);
                progress.Report(100);
            }

            return Task.FromResult(new TranscriptionEngineResult(
                [new TranscriptionEngineSegment(0.1, 1.2, "Hello from fake ASR.", 0.95)]));
        }
    }

    private sealed class FakeDiarizationEngine : IDiarizationEngine
    {
        public int CallCount { get; private set; }
        public bool ReportProgressEdges { get; init; }

        public Task<IReadOnlyList<DiarizationTurn>> DiarizeAsync(
            string normalizedWavPath,
            string modelPath,
            int? numSpeakers,
            double? clusterThreshold,
            IProgress<int> progress,
            CancellationToken cancellationToken)
        {
            CallCount++;
            if (ReportProgressEdges)
            {
                progress.Report(0);
                progress.Report(100);
            }

            return Task.FromResult<IReadOnlyList<DiarizationTurn>>([new DiarizationTurn(0.0, 1.5, "SPEAKER_00")]);
        }
    }

    private sealed class CollectingProgress : IProgress<int>
    {
        public List<int> Values { get; } = [];

        public void Report(int value)
        {
            Values.Add(value);
        }
    }
}

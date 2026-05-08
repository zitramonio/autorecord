using Autorecord.Core.Settings;
using Autorecord.Core.Transcription.Diarization;
using Autorecord.Core.Transcription.Engines;
using Autorecord.Core.Transcription.Jobs;
using Autorecord.Core.Transcription.Models;
using Autorecord.Core.Transcription.Results;

namespace Autorecord.Core.Transcription.Pipeline;

public sealed class TranscriptionPipeline : ITranscriptionPipeline
{
    private readonly ModelCatalog _catalog;
    private readonly ModelManager _modelManager;
    private readonly AudioNormalizer _audioNormalizer;
    private readonly IReadOnlyDictionary<string, ITranscriptionEngine> _asrEngines;
    private readonly IDiarizationEngine _diarizationEngine;
    private readonly TranscriptExporter _exporter;
    private readonly TranscriptionSettings _settings;

    public TranscriptionPipeline(
        ModelCatalog catalog,
        ModelManager modelManager,
        AudioNormalizer audioNormalizer,
        IReadOnlyDictionary<string, ITranscriptionEngine> asrEngines,
        IDiarizationEngine diarizationEngine,
        TranscriptExporter exporter,
        TranscriptionSettings settings)
    {
        _catalog = catalog;
        _modelManager = modelManager;
        _audioNormalizer = audioNormalizer;
        _asrEngines = asrEngines;
        _diarizationEngine = diarizationEngine;
        _exporter = exporter;
        _settings = settings;
    }

    public async Task<TranscriptionPipelineResult> RunAsync(
        TranscriptionJob job,
        IProgress<int> progress,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        LocalNetworkGuard.AssertTranscriptionRuntimeIsOffline();
        var pipelineProgress = new PipelineProgress(progress);
        pipelineProgress.Report(0);

        var asrModel = _catalog.GetRequired(job.AsrModelId);
        EnsureModelType(asrModel, "asr");
        await EnsureInstalledAsync(asrModel, cancellationToken);
        ModelCatalogEntry? diarizationModel = null;
        var diarizationModelId = job.DiarizationModelId;
        if (_settings.EnableDiarization)
        {
            if (string.IsNullOrWhiteSpace(diarizationModelId))
            {
                throw new InvalidOperationException("DiarizationModelId is required when diarization is enabled.");
            }

            diarizationModel = _catalog.GetRequired(diarizationModelId);
            EnsureModelType(diarizationModel, "diarization");
            await EnsureInstalledAsync(diarizationModel, cancellationToken);
        }

        if (!_asrEngines.TryGetValue(asrModel.Engine, out var asrEngine))
        {
            throw new InvalidOperationException($"ASR engine '{asrModel.Engine}' is not registered for model '{asrModel.Id}'.");
        }

        var normalized = await _audioNormalizer.NormalizeAsync(
            job.InputFilePath,
            _settings.KeepIntermediateFiles,
            cancellationToken);
        pipelineProgress.Report(10);

        IReadOnlyList<DiarizationTurn> diarizationTurns = [];
        if (diarizationModel is not null)
        {
            diarizationTurns = await _diarizationEngine.DiarizeAsync(
                normalized.NormalizedWavPath,
                _modelManager.GetModelPath(diarizationModel),
                _settings.NumSpeakers,
                _settings.ClusterThreshold,
                pipelineProgress.CreateStage(10, 45),
                cancellationToken);
        }

        var asrResult = await asrEngine.TranscribeAsync(
            normalized.NormalizedWavPath,
            _modelManager.GetModelPath(asrModel),
            pipelineProgress.CreateStage(diarizationModel is null ? 10 : 45, 95),
            cancellationToken);

        var segments = TranscriptAssembler.Assemble(asrResult.Segments, diarizationTurns);
        var document = new TranscriptDocument
        {
            InputFile = job.InputFilePath,
            DurationSec = segments.Count == 0 ? 0 : segments.Max(segment => segment.End),
            CreatedAt = DateTimeOffset.Now,
            AsrModelId = asrModel.Id,
            AsrModelDisplayName = asrModel.DisplayName,
            DiarizationModelId = diarizationModel?.Id,
            DiarizationModelDisplayName = diarizationModel?.DisplayName,
            Speakers = BuildSpeakers(segments),
            Segments = segments,
            RawDiarizationSegments = diarizationTurns
        };

        var outputFiles = await _exporter.ExportAsync(
            document,
            job.OutputDirectory,
            _settings.OutputFormats,
            _settings.OverwriteExistingTranscripts,
            cancellationToken);

        DeleteTemporaryNormalizedWavIfNeeded(normalized, job.InputFilePath);
        pipelineProgress.Report(100);

        return new TranscriptionPipelineResult(outputFiles.AllPaths, document.DurationSec);
    }

    private static void EnsureModelType(ModelCatalogEntry model, string expectedType)
    {
        if (!string.Equals(model.Type, expectedType, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Model '{model.Id}' must have type '{expectedType}', but catalog type is '{model.Type}'.");
        }
    }

    private async Task EnsureInstalledAsync(ModelCatalogEntry model, CancellationToken cancellationToken)
    {
        var status = await _modelManager.GetStatusAsync(model, cancellationToken);
        if (status != ModelInstallStatus.Installed)
        {
            throw new InvalidOperationException($"ModelNotInstalled: model '{model.Id}' status is '{status}'.");
        }
    }

    private static IReadOnlyList<TranscriptSpeaker> BuildSpeakers(IReadOnlyList<TranscriptSegment> segments)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var speakers = new List<TranscriptSpeaker>();

        foreach (var segment in segments)
        {
            if (seen.Add(segment.SpeakerId))
            {
                speakers.Add(new TranscriptSpeaker(segment.SpeakerId, segment.SpeakerLabel));
            }
        }

        return speakers;
    }

    private void DeleteTemporaryNormalizedWavIfNeeded(NormalizedAudio normalized, string inputPath)
    {
        if (_settings.KeepIntermediateFiles ||
            !normalized.CreatedTemporaryFile ||
            SamePath(normalized.NormalizedWavPath, inputPath) ||
            !File.Exists(normalized.NormalizedWavPath))
        {
            return;
        }

        File.Delete(normalized.NormalizedWavPath);
    }

    private static bool SamePath(string left, string right)
    {
        return string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);
    }

    private sealed class PipelineProgress
    {
        private readonly IProgress<int> _inner;
        private int _last;

        public PipelineProgress(IProgress<int> inner)
        {
            _inner = inner;
        }

        public void Report(int value)
        {
            var clamped = Math.Clamp(value, 0, 100);
            if (clamped < _last)
            {
                return;
            }

            _last = clamped;
            _inner.Report(clamped);
        }

        public IProgress<int> CreateStage(int start, int end)
        {
            return new StageProgress(this, start, end);
        }

        private sealed class StageProgress : IProgress<int>
        {
            private readonly PipelineProgress _parent;
            private readonly int _start;
            private readonly int _end;

            public StageProgress(PipelineProgress parent, int start, int end)
            {
                _parent = parent;
                _start = start;
                _end = end;
            }

            public void Report(int value)
            {
                var clamped = Math.Clamp(value, 0, 100);
                var mapped = _start + (int)Math.Round((_end - _start) * clamped / 100.0);
                _parent.Report(mapped);
            }
        }
    }
}

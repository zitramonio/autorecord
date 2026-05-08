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
        progress.Report(0);

        var asrModel = _catalog.GetRequired(job.AsrModelId);
        await EnsureInstalledAsync(asrModel, cancellationToken);

        if (!_asrEngines.TryGetValue(asrModel.Engine, out var asrEngine))
        {
            throw new InvalidOperationException($"ASR engine '{asrModel.Engine}' is not registered for model '{asrModel.Id}'.");
        }

        var normalized = await _audioNormalizer.NormalizeAsync(
            job.InputFilePath,
            _settings.KeepIntermediateFiles,
            cancellationToken);

        IReadOnlyList<DiarizationTurn> diarizationTurns = [];
        ModelCatalogEntry? diarizationModel = null;
        if (_settings.EnableDiarization && !string.IsNullOrWhiteSpace(job.DiarizationModelId))
        {
            diarizationModel = _catalog.GetRequired(job.DiarizationModelId);
            await EnsureInstalledAsync(diarizationModel, cancellationToken);

            diarizationTurns = await _diarizationEngine.DiarizeAsync(
                normalized.NormalizedWavPath,
                _modelManager.GetModelPath(diarizationModel),
                _settings.NumSpeakers,
                _settings.ClusterThreshold,
                progress,
                cancellationToken);
        }

        var asrResult = await asrEngine.TranscribeAsync(
            normalized.NormalizedWavPath,
            _modelManager.GetModelPath(asrModel),
            progress,
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
        progress.Report(100);

        return new TranscriptionPipelineResult(outputFiles.AllPaths);
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
}

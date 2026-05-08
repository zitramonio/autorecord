using System.Globalization;
using System.Text;
using Autorecord.Core.Transcription.Pipeline;

namespace Autorecord.Core.Transcription.Jobs;

public sealed class TranscriptionJobLogWriter
{
    private readonly string _logRoot;

    public TranscriptionJobLogWriter(string logRoot)
    {
        if (string.IsNullOrWhiteSpace(logRoot))
        {
            throw new ArgumentException("Log root must not be blank.", nameof(logRoot));
        }

        _logRoot = logRoot;
    }

    public string GetLogPath(Guid jobId)
    {
        return Path.Combine(_logRoot, $"transcription-job-{jobId}.log");
    }

    public Task WriteStartedAsync(TranscriptionJob job, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);

        var lines = new List<string>
        {
            $"JobId: {job.Id}",
            $"InputFilePath: {job.InputFilePath}",
            $"OutputDirectory: {job.OutputDirectory}",
            $"AsrModelId: {job.AsrModelId}",
            $"DiarizationModelId: {job.DiarizationModelId ?? "none"}",
            $"CreatedAt: {FormatDate(job.CreatedAt)}",
            $"StartedAt: {FormatDate(job.StartedAt)}"
        };

        return AppendLinesAsync(job.Id, lines, cancellationToken);
    }

    public Task WriteFinishedAsync(
        TranscriptionJob job,
        TranscriptionPipelineResult? result,
        TimeSpan processingTime,
        CancellationToken cancellationToken,
        double? fallbackDurationSec = null)
    {
        ArgumentNullException.ThrowIfNull(job);

        var lines = new List<string>
        {
            $"Status: {job.Status}",
            $"FinishedAt: {FormatDate(job.FinishedAt)}",
            $"DurationSec: {FormatNumber(result?.DurationSec ?? fallbackDurationSec)}",
            $"ProcessingTime: {processingTime.ToString("c", CultureInfo.InvariantCulture)}"
        };

        if (!string.IsNullOrWhiteSpace(job.ErrorMessage))
        {
            lines.Add($"Error: {job.ErrorMessage}");
        }

        foreach (var outputFile in job.OutputFiles)
        {
            lines.Add($"OutputFile: {outputFile}");
        }

        return AppendLinesAsync(job.Id, lines, cancellationToken);
    }

    private async Task AppendLinesAsync(Guid jobId, IReadOnlyList<string> lines, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_logRoot);
        var builder = new StringBuilder();
        foreach (var line in lines)
        {
            builder.AppendLine(line);
        }

        await File.AppendAllTextAsync(GetLogPath(jobId), builder.ToString(), Encoding.UTF8, cancellationToken);
    }

    private static string FormatDate(DateTimeOffset? value)
    {
        return value?.ToString("O", CultureInfo.InvariantCulture) ?? "unknown";
    }

    private static string FormatNumber(double? value)
    {
        return value?.ToString("0.###", CultureInfo.InvariantCulture) ?? "unknown";
    }
}

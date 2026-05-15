using System.Diagnostics;
using System.Text.Json;

namespace Autorecord.Core.Transcription.Engines;

public sealed class GigaAmWorkerClient
{
    private const int MaxErrorOutputLength = 4_000;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static TranscriptionEngineResult ParseResult(string json)
    {
        var dto = JsonSerializer.Deserialize<WorkerResultDto>(json, JsonOptions)
            ?? new WorkerResultDto();

        return new TranscriptionEngineResult(dto.Segments
            .Select(segment => new TranscriptionEngineSegment(segment.Start, segment.End, segment.Text, segment.Confidence))
            .ToList());
    }

    public async Task<TranscriptionEngineResult> RunAsync(
        string workerPath,
        string inputPath,
        string modelPath,
        string outputJsonPath,
        CancellationToken cancellationToken)
    {
        return await RunAsync(workerPath, inputPath, modelPath, outputJsonPath, null, cancellationToken);
    }

    public async Task<TranscriptionEngineResult> RunAsync(
        string workerPath,
        string inputPath,
        string modelPath,
        string outputJsonPath,
        IReadOnlyList<TranscriptionEngineInterval>? intervals,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var chunksJsonPath = await WriteChunksJsonAsync(intervals, cancellationToken);
        var startInfo = new ProcessStartInfo
        {
            FileName = workerPath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true
        };
        startInfo.ArgumentList.Add("--input");
        startInfo.ArgumentList.Add(inputPath);
        startInfo.ArgumentList.Add("--model");
        startInfo.ArgumentList.Add(modelPath);
        startInfo.ArgumentList.Add("--output-json");
        startInfo.ArgumentList.Add(outputJsonPath);
        if (chunksJsonPath is not null)
        {
            startInfo.ArgumentList.Add("--chunks-json");
            startInfo.ArgumentList.Add(chunksJsonPath);
        }

        try
        {
            using var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("Failed to start GigaAM worker process.");
            var stderrTask = process.StandardError.ReadToEndAsync();
            var stdoutTask = process.StandardOutput.ReadToEndAsync();

            try
            {
                await process.WaitForExitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                if (!process.HasExited)
                {
                    try
                    {
                        process.Kill(entireProcessTree: true);
                    }
                    catch (InvalidOperationException)
                    {
                        // The worker exited between the HasExited check and Kill.
                    }

                    await process.WaitForExitAsync(CancellationToken.None);
                }

                await Task.WhenAll(stderrTask, stdoutTask);
                throw;
            }

            var stderr = await stderrTask;
            await stdoutTask;

            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"GigaAM worker exited with code {process.ExitCode}.{FormatStderr(stderr)}");
            }

            var json = await File.ReadAllTextAsync(outputJsonPath, cancellationToken);
            return ParseResult(json);
        }
        finally
        {
            if (chunksJsonPath is not null && File.Exists(chunksJsonPath))
            {
                File.Delete(chunksJsonPath);
            }
        }
    }

    private static async Task<string?> WriteChunksJsonAsync(
        IReadOnlyList<TranscriptionEngineInterval>? intervals,
        CancellationToken cancellationToken)
    {
        if (intervals is null)
        {
            return null;
        }

        var chunksJsonPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.chunks.json");
        var json = JsonSerializer.Serialize(new WorkerChunksDto(intervals), JsonOptions);
        await File.WriteAllTextAsync(chunksJsonPath, json, cancellationToken);
        return chunksJsonPath;
    }

    private static string FormatStderr(string stderr)
    {
        if (string.IsNullOrWhiteSpace(stderr))
        {
            return "";
        }

        var trimmed = stderr.Trim();
        if (trimmed.Length > MaxErrorOutputLength)
        {
            trimmed = trimmed[..MaxErrorOutputLength] + "...";
        }

        return $" Stderr: {trimmed}";
    }

    private sealed record WorkerResultDto
    {
        public IReadOnlyList<WorkerSegmentDto> Segments { get; init; } = [];
    }

    private sealed record WorkerChunksDto(IReadOnlyList<TranscriptionEngineInterval> Chunks);

    private sealed record WorkerSegmentDto
    {
        public double Start { get; init; }
        public double End { get; init; }
        public string Text { get; init; } = "";
        public double? Confidence { get; init; }
    }
}

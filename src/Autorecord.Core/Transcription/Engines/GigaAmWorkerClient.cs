using System.Diagnostics;
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

    public async Task<TranscriptionEngineResult> RunAsync(
        string workerPath,
        string inputPath,
        string modelPath,
        string outputJsonPath,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = workerPath,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("--input");
        startInfo.ArgumentList.Add(inputPath);
        startInfo.ArgumentList.Add("--model");
        startInfo.ArgumentList.Add(modelPath);
        startInfo.ArgumentList.Add("--output-json");
        startInfo.ArgumentList.Add(outputJsonPath);

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start GigaAM worker process.");

        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }

            throw;
        }

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"GigaAM worker exited with code {process.ExitCode}.");
        }

        var json = await File.ReadAllTextAsync(outputJsonPath, cancellationToken);
        return ParseResult(json);
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

using System.Diagnostics;
using System.Text.Json;
using Autorecord.Core.Transcription.Results;

namespace Autorecord.Core.Transcription.Diarization;

public sealed class PyannoteCommunityWorkerClient
{
    private const int MaxErrorOutputLength = 4_000;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static IReadOnlyList<DiarizationTurn> ParseResult(string json)
    {
        var dto = JsonSerializer.Deserialize<WorkerResultDto>(json, JsonOptions)
            ?? new WorkerResultDto();

        return dto.Turns
            .Select(turn => new DiarizationTurn(turn.Start, turn.End, turn.SpeakerId))
            .ToArray();
    }

    public async Task<IReadOnlyList<DiarizationTurn>> RunAsync(
        string workerPath,
        string inputPath,
        string modelPath,
        string outputJsonPath,
        int? numSpeakers,
        double? clusterThreshold,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var startInfo = new ProcessStartInfo
        {
            FileName = workerPath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true
        };
        startInfo.Environment["HF_HUB_OFFLINE"] = "1";
        startInfo.Environment["TRANSFORMERS_OFFLINE"] = "1";
        startInfo.Environment["HF_HUB_DISABLE_TELEMETRY"] = "1";
        startInfo.Environment["PYANNOTE_METRICS_ENABLED"] = "false";
        startInfo.ArgumentList.Add("--input");
        startInfo.ArgumentList.Add(inputPath);
        startInfo.ArgumentList.Add("--model");
        startInfo.ArgumentList.Add(modelPath);
        startInfo.ArgumentList.Add("--output-json");
        startInfo.ArgumentList.Add(outputJsonPath);

        if (numSpeakers is not null)
        {
            startInfo.ArgumentList.Add("--num-speakers");
            startInfo.ArgumentList.Add(numSpeakers.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        if (clusterThreshold is not null)
        {
            startInfo.ArgumentList.Add("--cluster-threshold");
            startInfo.ArgumentList.Add(clusterThreshold.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start Pyannote Community-1 worker process.");
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
                $"Pyannote Community-1 worker exited with code {process.ExitCode}.{FormatStderr(stderr)}");
        }

        var json = await File.ReadAllTextAsync(outputJsonPath, cancellationToken);
        return ParseResult(json);
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
        public IReadOnlyList<WorkerTurnDto> Turns { get; init; } = [];
    }

    private sealed record WorkerTurnDto
    {
        public double Start { get; init; }
        public double End { get; init; }
        public string SpeakerId { get; init; } = "";
    }
}

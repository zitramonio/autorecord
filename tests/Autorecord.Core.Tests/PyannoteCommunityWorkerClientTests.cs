using System.Diagnostics;
using Autorecord.Core.Transcription.Diarization;

namespace Autorecord.Core.Tests;

public sealed class PyannoteCommunityWorkerClientTests
{
    [Fact]
    public void WorkerSourceDoesNotUseTorchaudioForAudioDecoding()
    {
        var workerPath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "..",
            "tools",
            "pyannote-community-worker",
            "worker.py"));
        var source = File.ReadAllText(workerPath);

        Assert.DoesNotContain("import torchaudio", source, StringComparison.Ordinal);
        Assert.DoesNotContain("torchaudio.load", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ParseResultReadsTurns()
    {
        var json = """
        {
          "turns": [
            { "start": 1.2, "end": 3.4, "speakerId": "SPEAKER_00" }
          ]
        }
        """;

        var turns = PyannoteCommunityWorkerClient.ParseResult(json);

        Assert.Single(turns);
        Assert.Equal(1.2, turns[0].Start);
        Assert.Equal(3.4, turns[0].End);
        Assert.Equal("SPEAKER_00", turns[0].SpeakerId);
    }

    [Fact]
    public async Task RunAsyncReadsWorkerOutputJson()
    {
        var root = CreateTempDirectory();
        try
        {
            var workerPath = Path.Combine(root, "fake-pyannote-worker.cmd");
            var inputPath = Path.Combine(root, "input.wav");
            var modelPath = Path.Combine(root, "model");
            var outputJsonPath = Path.Combine(root, "result.json");
            Directory.CreateDirectory(modelPath);
            await File.WriteAllTextAsync(inputPath, "");
            await File.WriteAllTextAsync(
                workerPath,
                """
                @echo off
                set output=
                :args
                if "%~1"=="" goto write
                if "%~1"=="--output-json" (
                  set output=%~2
                  shift
                )
                shift
                goto args
                :write
                > "%output%" echo {"turns":[{"start":0,"end":1,"speakerId":"SPEAKER_01"}]}
                exit /b 0
                """);

            var client = new PyannoteCommunityWorkerClient();

            var turns = await client.RunAsync(
                workerPath,
                inputPath,
                modelPath,
                outputJsonPath,
                numSpeakers: 2,
                clusterThreshold: null,
                CancellationToken.None);

            Assert.Single(turns);
            Assert.Equal("SPEAKER_01", turns[0].SpeakerId);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsyncKillsWorkerProcessOnCancellation()
    {
        var root = CreateTempDirectory();
        try
        {
            var workerPath = Path.Combine(root, "fake-pyannote-worker.cmd");
            var inputPath = Path.Combine(root, "input.wav");
            var modelPath = Path.Combine(root, "model");
            var outputJsonPath = Path.Combine(root, "result.json");
            var markerPath = Path.Combine(root, "started.marker");
            var pidPath = Path.Combine(root, "worker.pid");
            Directory.CreateDirectory(modelPath);
            await File.WriteAllTextAsync(inputPath, "");
            await File.WriteAllTextAsync(
                workerPath,
                $"""
                @echo off
                powershell -NoProfile -ExecutionPolicy Bypass -Command "$PID | Set-Content -LiteralPath '{pidPath}'; 'started' | Set-Content -LiteralPath '{markerPath}'; Start-Sleep -Seconds 30"
                """);

            var client = new PyannoteCommunityWorkerClient();
            using var cancellation = new CancellationTokenSource();
            var runTask = client.RunAsync(
                workerPath,
                inputPath,
                modelPath,
                outputJsonPath,
                numSpeakers: null,
                clusterThreshold: null,
                cancellation.Token);

            await WaitForFileAsync(markerPath, CancellationToken.None);
            var workerProcessId = int.Parse(await File.ReadAllTextAsync(pidPath));
            await cancellation.CancelAsync();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runTask);

            Assert.True(
                await WaitForProcessExitAsync(workerProcessId, TimeSpan.FromSeconds(5)),
                "Expected cancelled worker process to exit.");
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    private static async Task WaitForFileAsync(string path, CancellationToken cancellationToken)
    {
        var timeoutAt = DateTimeOffset.UtcNow.AddSeconds(5);
        while (!File.Exists(path))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (DateTimeOffset.UtcNow > timeoutAt)
            {
                throw new TimeoutException($"Timed out waiting for file: {path}");
            }

            await Task.Delay(50, cancellationToken);
        }
    }

    private static async Task<bool> WaitForProcessExitAsync(int processId, TimeSpan timeout)
    {
        var timeoutAt = DateTimeOffset.UtcNow.Add(timeout);
        while (DateTimeOffset.UtcNow <= timeoutAt)
        {
            try
            {
                using var process = Process.GetProcessById(processId);
                if (process.HasExited)
                {
                    return true;
                }
            }
            catch (ArgumentException)
            {
                return true;
            }

            await Task.Delay(50);
        }

        return false;
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }
}

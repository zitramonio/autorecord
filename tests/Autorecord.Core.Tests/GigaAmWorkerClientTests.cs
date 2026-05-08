using System.Diagnostics;
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
        var segment = result.Segments[0];
        Assert.Equal(1.2, segment.Start);
        Assert.Equal(3.4, segment.End);
        Assert.Equal("Привет", segment.Text);
        Assert.Null(segment.Confidence);
    }

    [Fact]
    public async Task RunAsyncReadsWorkerOutputJson()
    {
        var root = CreateTempDirectory();
        try
        {
            var pathsRoot = Path.Combine(root, "paths with spaces");
            Directory.CreateDirectory(pathsRoot);
            var workerPath = Path.Combine(pathsRoot, "fake worker.cmd");
            var inputPath = Path.Combine(pathsRoot, "input file.wav");
            var modelPath = Path.Combine(pathsRoot, "model folder");
            var outputJsonPath = Path.Combine(pathsRoot, "result file.json");
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
                > "%output%" echo {"segments":[{"start":0,"end":1,"text":"Готово","confidence":0.9}]}
                exit /b 0
                """);

            var client = new GigaAmWorkerClient();

            var result = await client.RunAsync(workerPath, inputPath, modelPath, outputJsonPath, CancellationToken.None);

            Assert.Single(result.Segments);
            Assert.Equal("Готово", result.Segments[0].Text);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsyncThrowsWithStderrWhenWorkerExitsWithError()
    {
        var root = CreateTempDirectory();
        try
        {
            var workerPath = Path.Combine(root, "fake-worker.cmd");
            var inputPath = Path.Combine(root, "input.wav");
            var modelPath = Path.Combine(root, "model");
            var outputJsonPath = Path.Combine(root, "result.json");
            Directory.CreateDirectory(modelPath);
            await File.WriteAllTextAsync(inputPath, "");
            await File.WriteAllTextAsync(
                workerPath,
                """
                @echo off
                powershell -NoProfile -ExecutionPolicy Bypass -Command "[Console]::Out.WriteLine('transcript stdout text'); [Console]::Error.WriteLine('worker stderr details ' + ('x' * 4100) + ' stderr tail')"
                exit /b 7
                """);

            var client = new GigaAmWorkerClient();

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => client.RunAsync(workerPath, inputPath, modelPath, outputJsonPath, CancellationToken.None));

            Assert.Contains("7", exception.Message, StringComparison.Ordinal);
            Assert.Contains("worker stderr details", exception.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("stderr tail", exception.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("transcript stdout text", exception.Message, StringComparison.Ordinal);
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
            var workerPath = Path.Combine(root, "fake-worker.cmd");
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

            var client = new GigaAmWorkerClient();
            using var cancellation = new CancellationTokenSource();
            var runTask = client.RunAsync(workerPath, inputPath, modelPath, outputJsonPath, cancellation.Token);

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

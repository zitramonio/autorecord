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

    [Fact]
    public async Task RunAsyncReadsWorkerOutputJson()
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
    public async Task RunAsyncThrowsWhenWorkerExitsWithError()
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
                exit /b 7
                """);

            var client = new GigaAmWorkerClient();

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => client.RunAsync(workerPath, inputPath, modelPath, outputJsonPath, CancellationToken.None));

            Assert.Contains("7", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            DeleteDirectory(root);
        }
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

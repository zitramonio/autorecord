using Autorecord.Core.Transcription.Engines;

namespace Autorecord.Core.Tests;

public sealed class SherpaOnnxTranscriptionEngineTests
{
    [Fact]
    public async Task TranscribeAsyncThrowsWhenRequiredModelFilesAreMissing()
    {
        var root = CreateTempDirectory();
        try
        {
            var engine = new SherpaOnnxTranscriptionEngine();

            var exception = await Assert.ThrowsAsync<FileNotFoundException>(
                () => engine.TranscribeAsync("normalized.wav", root, new Progress<int>(), CancellationToken.None));

            Assert.Contains("tokens.txt", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task TranscribeAsyncThrowsWhenModelFileIsMissing()
    {
        var root = CreateTempDirectory();
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "tokens.txt"), "test");
            var engine = new SherpaOnnxTranscriptionEngine();

            var exception = await Assert.ThrowsAsync<FileNotFoundException>(
                () => engine.TranscribeAsync("normalized.wav", root, new Progress<int>(), CancellationToken.None));

            Assert.Contains("model.int8.onnx", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task TranscribeAsyncHonorsCancellationBeforeValidation()
    {
        var root = CreateTempDirectory();
        try
        {
            var engine = new SherpaOnnxTranscriptionEngine();
            using var cancellation = new CancellationTokenSource();
            await cancellation.CancelAsync();

            await Assert.ThrowsAsync<OperationCanceledException>(
                () => engine.TranscribeAsync("normalized.wav", root, new Progress<int>(), cancellation.Token));
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

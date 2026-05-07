using Autorecord.Core.Transcription.Models;

namespace Autorecord.Core.Tests;

public sealed class ModelCatalogTests
{
    [Fact]
    public async Task LoadAsyncLoadsAsrAndDiarizationModels()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "catalog.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(
            path,
            """
            {
              "models": [
                {
                  "id": "asr-fast",
                  "displayName": "ASR fast",
                  "description": "Fast ASR model",
                  "type": "asr",
                  "engine": "sherpa-onnx",
                  "language": "ru",
                  "version": "1",
                  "sizeMb": 100,
                  "requiresDiarization": false,
                  "download": {
                    "url": "https://example.com/asr.tar.bz2",
                    "archiveType": "tar.bz2",
                    "sha256": null
                  },
                  "install": {
                    "targetFolder": "asr-fast",
                    "requiredFiles": [
                      "model.onnx",
                      "tokens.txt"
                    ]
                  },
                  "runtime": {
                    "sampleRate": 16000,
                    "channels": 1,
                    "device": "cpu"
                  }
                },
                {
                  "id": "diarization-fast",
                  "displayName": "Diarization fast",
                  "description": "Fast diarization model",
                  "type": "diarization",
                  "engine": "sherpa-onnx",
                  "language": "ru",
                  "version": "1",
                  "requiresDiarization": true,
                  "download": {
                    "segmentationUrl": "https://example.com/segmentation.tar.bz2",
                    "embeddingUrl": "https://example.com/embedding.onnx"
                  },
                  "install": {
                    "targetFolder": "diarization-fast",
                    "requiredFiles": [
                      "model.onnx"
                    ]
                  },
                  "runtime": {}
                }
              ]
            }
            """);

        var catalog = await ModelCatalog.LoadAsync(path, CancellationToken.None);

        Assert.Equal(2, catalog.Models.Count);
        Assert.Single(catalog.GetByType("ASR"));
        Assert.Single(catalog.GetByType("diarization"));
        Assert.Equal("ASR fast", catalog.GetRequired("ASR-FAST").DisplayName);
    }

    [Fact]
    public async Task LoadAsyncRejectsDuplicateModelIds()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "catalog.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(
            path,
            """
            {
              "models": [
                {
                  "id": "duplicate",
                  "displayName": "First",
                  "type": "asr",
                  "engine": "sherpa-onnx",
                  "install": {
                    "targetFolder": "first"
                  }
                },
                {
                  "id": "DUPLICATE",
                  "displayName": "Second",
                  "type": "asr",
                  "engine": "sherpa-onnx",
                  "install": {
                    "targetFolder": "second"
                  }
                }
              ]
            }
            """);

        await Assert.ThrowsAsync<InvalidOperationException>(() => ModelCatalog.LoadAsync(path, CancellationToken.None));
    }

    [Fact]
    public async Task GetRequiredMissingIdThrowsInvalidOperationException()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "catalog.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(
            path,
            """
            {
              "models": [
                {
                  "id": "asr-fast",
                  "displayName": "ASR fast",
                  "type": "asr",
                  "engine": "sherpa-onnx",
                  "install": {
                    "targetFolder": "asr-fast"
                  }
                }
              ]
            }
            """);
        var catalog = await ModelCatalog.LoadAsync(path, CancellationToken.None);

        Assert.Throws<InvalidOperationException>(() => catalog.GetRequired("missing"));
    }
}

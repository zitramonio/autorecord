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

    [Theory]
    [InlineData("null")]
    [InlineData("""{ "models": null }""")]
    public async Task LoadAsyncRejectsNullModelsArray(string json)
    {
        var path = await WriteCatalogAsync(json);

        await Assert.ThrowsAsync<InvalidOperationException>(() => ModelCatalog.LoadAsync(path, CancellationToken.None));
    }

    [Theory]
    [InlineData("""{ "models": [null] }""")]
    [InlineData("""{ "models": [{ "id": "asr", "displayName": "ASR", "type": "asr", "engine": "sherpa-onnx", "download": null, "install": { "targetFolder": "asr" } }] }""")]
    [InlineData("""{ "models": [{ "id": "asr", "displayName": "ASR", "type": "asr", "engine": "sherpa-onnx", "install": null }] }""")]
    [InlineData("""{ "models": [{ "id": "asr", "displayName": "ASR", "type": "asr", "engine": "sherpa-onnx", "install": { "targetFolder": "asr" }, "runtime": null }] }""")]
    public async Task LoadAsyncRejectsNullNestedObjects(string json)
    {
        var path = await WriteCatalogAsync(json);

        await Assert.ThrowsAsync<InvalidOperationException>(() => ModelCatalog.LoadAsync(path, CancellationToken.None));
    }

    [Theory]
    [InlineData("""{ "models": [{ "id": "asr", "displayName": "ASR", "type": "asr", "engine": "sherpa-onnx", "install": { "targetFolder": "asr", "requiredFiles": null } }] }""")]
    [InlineData("""{ "models": [{ "id": "asr", "displayName": "ASR", "type": "asr", "engine": "sherpa-onnx", "install": { "targetFolder": "asr", "requiredFiles": [null] } }] }""")]
    [InlineData("""{ "models": [{ "id": "asr", "displayName": "ASR", "type": "asr", "engine": "sherpa-onnx", "install": { "targetFolder": "asr", "requiredFiles": [""] } }] }""")]
    [InlineData("""{ "models": [{ "id": "asr", "displayName": "ASR", "type": "asr", "engine": "sherpa-onnx", "install": { "targetFolder": "asr", "requiredFiles": [" model.onnx"] } }] }""")]
    public async Task LoadAsyncRejectsNullRequiredFiles(string json)
    {
        var path = await WriteCatalogAsync(json);

        await Assert.ThrowsAsync<InvalidOperationException>(() => ModelCatalog.LoadAsync(path, CancellationToken.None));
    }

    [Theory]
    [InlineData(" id", "Name", "asr", "sherpa-onnx", "folder")]
    [InlineData("id", "Name ", "asr", "sherpa-onnx", "folder")]
    [InlineData("id", "Name", " asr", "sherpa-onnx", "folder")]
    [InlineData("id", "Name", "asr", "sherpa-onnx ", "folder")]
    [InlineData("id", "Name", "asr", "sherpa-onnx", " folder")]
    public async Task LoadAsyncRejectsWhitespaceInModelId(
        string id,
        string displayName,
        string type,
        string engine,
        string targetFolder)
    {
        var path = await WriteCatalogAsync(
            $$"""
            {
              "models": [
                {
                  "id": "{{id}}",
                  "displayName": "{{displayName}}",
                  "type": "{{type}}",
                  "engine": "{{engine}}",
                  "install": {
                    "targetFolder": "{{targetFolder}}"
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

    [Fact]
    public async Task BundledCatalogMakesGigaAmV3Downloadable()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "models", "catalog.json");

        var catalog = await ModelCatalog.LoadAsync(path, CancellationToken.None);
        var model = catalog.GetRequired("gigaam-v3-ru-quality");

        Assert.Equal("gigaam-v3", model.Engine);
        Assert.Contains("v3_e2e_rnnt.ckpt", model.Install.RequiredFiles);
        Assert.Contains("v3_e2e_rnnt_tokenizer.model", model.Install.RequiredFiles);
        Assert.Contains("v3_e2e_rnnt.ckpt", model.Download.Url, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("v3_e2e_rnnt_tokenizer.model", model.Download.EmbeddingUrl, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task BundledCatalogContainsReleaseModels()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "models", "catalog.json");

        var catalog = await ModelCatalog.LoadAsync(path, CancellationToken.None);

        Assert.Equal(
            ["gigaam-v3-ru-quality", "pyannote-community-1"],
            catalog.Models.Select(model => model.Id).ToArray());
        Assert.Single(catalog.GetByType("asr"));
        Assert.Single(catalog.GetByType("diarization"));
    }

    [Fact]
    public async Task BundledCatalogDoesNotOfferParakeetInPublicRelease()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "models", "catalog.json");

        var catalog = await ModelCatalog.LoadAsync(path, CancellationToken.None);

        Assert.DoesNotContain(catalog.Models, model =>
            model.Id.Contains("parakeet", StringComparison.OrdinalIgnoreCase) ||
            model.DisplayName.Contains("Parakeet", StringComparison.OrdinalIgnoreCase) ||
            (model.Download.Url?.Contains("parakeet", StringComparison.OrdinalIgnoreCase) ?? false));
    }

    [Fact]
    public async Task BundledCatalogContainsPyannoteCommunity1()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "models", "catalog.json");

        var catalog = await ModelCatalog.LoadAsync(path, CancellationToken.None);
        var model = catalog.GetRequired("pyannote-community-1");

        Assert.Equal("diarization", model.Type);
        Assert.Equal("pyannote-community-1", model.Engine);
        Assert.Equal("pyannote/speaker-diarization-community-1", model.Download.HuggingFaceRepoId);
        Assert.True(model.Download.RequiresAuthorization);
        Assert.Contains("config.yaml", model.Install.RequiredFiles);
        Assert.Contains("segmentation/pytorch_model.bin", model.Install.RequiredFiles);
        Assert.Contains("embedding/pytorch_model.bin", model.Install.RequiredFiles);
        Assert.Contains("plda/plda.npz", model.Install.RequiredFiles);
        Assert.Contains("plda/xvec_transform.npz", model.Install.RequiredFiles);
    }

    private static async Task<string> WriteCatalogAsync(string json)
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "catalog.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, json);
        return path;
    }
}

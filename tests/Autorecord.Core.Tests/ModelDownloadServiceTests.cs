using System.Net;
using Autorecord.Core.Transcription.Models;

namespace Autorecord.Core.Tests;

public sealed class ModelDownloadServiceTests
{
    [Fact]
    public void PercentClampsBeforeIntegerCast()
    {
        var progress = new ModelDownloadProgress
        {
            BytesDownloaded = long.MaxValue,
            TotalBytes = 1,
            BytesPerSecond = null
        };

        Assert.Equal(100, progress.Percent);
    }

    [Fact]
    public async Task DownloadAsyncThrowsClearErrorForServerFailure()
    {
        var service = CreateService(
            CreateTempRoot(),
            _ => new HttpResponseMessage(HttpStatusCode.InternalServerError));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.DownloadAsync(CreateModel(), null, CancellationToken.None));

        Assert.Contains("HTTP 500", error.Message);
    }

    [Fact]
    public async Task DownloadAsyncWritesTempFileAndReportsProgress()
    {
        var root = CreateTempRoot();
        var content = new byte[] { 1, 2, 3, 4, 5 };
        var progress = new List<ModelDownloadProgress>();
        var service = CreateService(
            root,
            _ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(content)
            });

        var path = await service.DownloadAsync(
            CreateModel(id: "asr-fast"),
            new CollectingProgress(progress),
            CancellationToken.None);

        Assert.Equal(content, await File.ReadAllBytesAsync(path));
        Assert.StartsWith(Path.GetFullPath(root), Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase);
        Assert.Contains($"{Path.DirectorySeparatorChar}asr-fast.", path);
        Assert.NotEmpty(progress);
        Assert.Equal(content.Length, progress[^1].BytesDownloaded);
        Assert.Equal(content.Length, progress[^1].TotalBytes);
        Assert.Equal(100, progress[^1].Percent);
        Assert.True(progress[^1].BytesPerSecond >= 0);
    }

    [Fact]
    public async Task DownloadAsyncDeletesTempFileOnFailure()
    {
        var root = CreateTempRoot();
        var service = CreateService(
            root,
            _ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(new ThrowingReadStream(new byte[] { 1, 2, 3 }))
            });

        await Assert.ThrowsAsync<IOException>(
            () => service.DownloadAsync(CreateModel(id: "asr-fast"), null, CancellationToken.None));

        Assert.Empty(Directory.GetFiles(root, "*.download"));
    }

    [Fact]
    public async Task DownloadAsyncDeletesTempFileOnCancellation()
    {
        var root = CreateTempRoot();
        using var cancellation = new CancellationTokenSource();
        var service = CreateService(
            root,
            _ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(new CancelingReadStream(new byte[] { 1, 2, 3 }, cancellation.Cancel))
            });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.DownloadAsync(CreateModel(id: "asr-fast"), null, cancellation.Token));

        Assert.Empty(Directory.GetFiles(root, "*.download"));
    }

    [Fact]
    public async Task DownloadAsyncThrowsWhenDownloadUnavailable()
    {
        var service = CreateService(
            CreateTempRoot(),
            _ => new HttpResponseMessage(HttpStatusCode.OK));
        var model = CreateModel(download: new ModelDownloadInfo { Url = " ", SegmentationUrl = "" });

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.DownloadAsync(model, null, CancellationToken.None));

        Assert.Contains("No download URL", error.Message);
    }

    [Fact]
    public async Task DownloadAsyncKeepsTempFileInsideDownloadsRootForUnsafeModelId()
    {
        var root = CreateTempRoot();
        var outside = Path.Combine(Path.GetDirectoryName(root)!, "outside-marker.download");
        var service = CreateService(
            root,
            _ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent([1])
            });

        var path = await service.DownloadAsync(
            CreateModel(id: Path.Combine("..", "outside-marker")),
            null,
            CancellationToken.None);

        Assert.StartsWith(Path.GetFullPath(root), Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(outside));
    }

    [Fact]
    public async Task DownloadFileAsyncDownloadsExplicitUrlWithFileNameHint()
    {
        var root = CreateTempRoot();
        var requestedUris = new List<Uri?>();
        var service = CreateService(
            root,
            request =>
            {
                requestedUris.Add(request.RequestUri);
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent([9, 8, 7])
                };
            });

        var path = await service.DownloadFileAsync(
            "https://example.com/embedding.onnx",
            "embedding.onnx",
            null,
            CancellationToken.None);

        Assert.Equal(new Uri("https://example.com/embedding.onnx"), Assert.Single(requestedUris));
        Assert.Equal([9, 8, 7], await File.ReadAllBytesAsync(path));
        Assert.Contains($"{Path.DirectorySeparatorChar}embedding.onnx.", path);
        Assert.StartsWith(Path.GetFullPath(root), Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DownloadAsyncThrowsNotEnoughDiskSpaceBeforeWritingTempFile()
    {
        var root = CreateTempRoot();
        var service = CreateService(
            root,
            _ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(new byte[1024])
            },
            _ => 10);

        var error = await Assert.ThrowsAsync<NotEnoughDiskSpaceException>(
            () => service.DownloadAsync(CreateModel(), null, CancellationToken.None));

        Assert.Contains("Недостаточно места", error.Message);
        Assert.Empty(Directory.GetFiles(root, "*.download"));
    }

    private static ModelDownloadService CreateService(
        string root,
        Func<HttpRequestMessage, HttpResponseMessage> responseFactory,
        Func<string, long?>? getAvailableFreeSpaceBytes = null)
    {
        return new ModelDownloadService(
            new HttpClient(new FakeHttpMessageHandler(responseFactory)),
            root,
            getAvailableFreeSpaceBytes);
    }

    private static string CreateTempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static ModelCatalogEntry CreateModel(string id = "asr-fast", ModelDownloadInfo? download = null)
    {
        return new ModelCatalogEntry
        {
            Id = id,
            DisplayName = id,
            Type = "asr",
            Engine = "sherpa-onnx",
            Download = download ?? new ModelDownloadInfo { Url = "https://example.com/model.tar.bz2" }
        };
    }

    private sealed class FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(responseFactory(request));
        }
    }

    private sealed class CollectingProgress(List<ModelDownloadProgress> values) : IProgress<ModelDownloadProgress>
    {
        public void Report(ModelDownloadProgress value)
        {
            values.Add(value);
        }
    }

    private sealed class ThrowingReadStream(byte[] firstChunk) : Stream
    {
        private bool _hasRead;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_hasRead)
            {
                throw new IOException("read failed");
            }

            _hasRead = true;
            var bytesToCopy = Math.Min(count, firstChunk.Length);
            Array.Copy(firstChunk, 0, buffer, offset, bytesToCopy);
            return bytesToCopy;
        }

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            if (_hasRead)
            {
                throw new IOException("read failed");
            }

            _hasRead = true;
            firstChunk.CopyTo(buffer);
            return ValueTask.FromResult(firstChunk.Length);
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class CancelingReadStream(byte[] firstChunk, Action cancel) : Stream
    {
        private bool _hasRead;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            if (_hasRead)
            {
                cancellationToken.ThrowIfCancellationRequested();
                throw new OperationCanceledException(cancellationToken);
            }

            _hasRead = true;
            firstChunk.CopyTo(buffer);
            cancel();
            return ValueTask.FromResult(firstChunk.Length);
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}

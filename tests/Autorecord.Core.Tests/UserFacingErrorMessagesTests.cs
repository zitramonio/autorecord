using Autorecord.Core.Transcription.Models;
using Autorecord.Core.Transcription.Pipeline;
using Autorecord.Core.Transcription;

namespace Autorecord.Core.Tests;

public sealed class UserFacingErrorMessagesTests
{
    [Fact]
    public void ForModelDownloadMapsDiskSpaceError()
    {
        var message = UserFacingErrorMessages.ForModelDownload(
            new NotEnoughDiskSpaceException(1024, 10));

        Assert.Equal("Недостаточно места на диске для скачивания модели или сохранения транскрипта.", message);
    }

    [Fact]
    public void ForModelDownloadMapsNetworkError()
    {
        var message = UserFacingErrorMessages.ForModelDownload(
            new HttpRequestException("network down"));

        Assert.Equal("Не удалось скачать модель. Проверьте интернет и свободное место на диске.", message);
    }

    [Fact]
    public void ForModelDownloadMapsValidationError()
    {
        var message = UserFacingErrorMessages.ForModelDownload(
            new InvalidOperationException("Model artifact sha256 mismatch."));

        Assert.Equal("Модель скачана, но не прошла проверку. Попробуйте скачать её заново.", message);
    }

    [Fact]
    public void ForTranscriptionMapsMissingModelError()
    {
        var message = UserFacingErrorMessages.ForTranscription(
            new TranscriptionModelNotInstalledException("asr-fast", "NotInstalled"));

        Assert.Equal("Модель не установлена. Скачайте модель во вкладке Транскрибация.", message);
    }

    [Fact]
    public void ForTranscriptionMapsOutputFolderError()
    {
        var message = UserFacingErrorMessages.ForTranscription(
            new UnauthorizedAccessException("denied"));

        Assert.Equal("Папка для транскриптов недоступна. Выберите другую папку.", message);
    }
}

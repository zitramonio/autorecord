using Autorecord.Core.Transcription.Models;
using Autorecord.Core.Transcription.Pipeline;

namespace Autorecord.Core.Transcription;

public static class UserFacingErrorMessages
{
    public static string ForModelDownload(Exception exception)
    {
        return exception switch
        {
            NotEnoughDiskSpaceException => "Недостаточно места на диске для скачивания модели или сохранения транскрипта.",
            HttpRequestException => "Не удалось скачать модель. Проверьте интернет и свободное место на диске.",
            IOException => "Не удалось скачать модель. Проверьте интернет и свободное место на диске.",
            InvalidOperationException invalidOperation when IsHuggingFaceAuthorizationError(invalidOperation) =>
                "Ошибка - неверный токен",
            InvalidOperationException invalidOperation when IsModelValidationError(invalidOperation) =>
                "Модель скачана, но не прошла проверку. Попробуйте скачать её заново.",
            InvalidOperationException => "Не удалось скачать модель. Проверьте интернет и свободное место на диске.",
            _ => exception.Message
        };
    }

    public static string ForModelValidation(Exception exception)
    {
        return exception switch
        {
            UnauthorizedAccessException => "Модель скачана, но не прошла проверку. Попробуйте скачать её заново.",
            IOException => "Модель скачана, но не прошла проверку. Попробуйте скачать её заново.",
            InvalidOperationException => "Модель скачана, но не прошла проверку. Попробуйте скачать её заново.",
            _ => exception.Message
        };
    }

    public static string ForTranscription(Exception exception)
    {
        return exception switch
        {
            TranscriptionModelNotInstalledException => "Модель не установлена. Скачайте модель во вкладке Транскрибация.",
            UnauthorizedAccessException => "Папка для транскриптов недоступна. Выберите другую папку.",
            IOException => "Папка для транскриптов недоступна. Выберите другую папку.",
            InvalidDataException => "Формат файла не поддерживается. Для версии 2 гарантированно поддерживается .wav.",
            NotSupportedException => "Формат файла не поддерживается. Для версии 2 гарантированно поддерживается .wav.",
            _ => "Не удалось выполнить транскрибацию. Запись сохранена, её можно обработать повторно."
        };
    }

    private static bool IsModelValidationError(Exception exception)
    {
        return exception.Message.Contains("sha256", StringComparison.OrdinalIgnoreCase) ||
            exception.Message.Contains("required file", StringComparison.OrdinalIgnoreCase) ||
            exception.Message.Contains("validation", StringComparison.OrdinalIgnoreCase) ||
            exception.Message.Contains("провер", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsHuggingFaceAuthorizationError(Exception exception)
    {
        return exception.Message.Contains("HTTP 401", StringComparison.OrdinalIgnoreCase) ||
            exception.Message.Contains("HTTP 403", StringComparison.OrdinalIgnoreCase) ||
            exception.Message.Contains("Unauthorized", StringComparison.OrdinalIgnoreCase) ||
            exception.Message.Contains("Forbidden", StringComparison.OrdinalIgnoreCase);
    }
}

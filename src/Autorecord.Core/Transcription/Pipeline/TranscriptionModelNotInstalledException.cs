namespace Autorecord.Core.Transcription.Pipeline;

public sealed class TranscriptionModelNotInstalledException : Exception
{
    public TranscriptionModelNotInstalledException(string modelId, string status)
        : base("Модель не установлена. Скачайте модель во вкладке Транскрибация.")
    {
        ModelId = modelId;
        Status = status;
    }

    public string ModelId { get; }

    public string Status { get; }
}

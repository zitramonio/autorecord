namespace Autorecord.Core.Transcription.Models;

public sealed class NotEnoughDiskSpaceException : IOException
{
    public NotEnoughDiskSpaceException(long requiredBytes, long availableBytes)
        : base("Недостаточно места на диске для скачивания модели или сохранения транскрипта.")
    {
        RequiredBytes = requiredBytes;
        AvailableBytes = availableBytes;
    }

    public long RequiredBytes { get; }

    public long AvailableBytes { get; }
}

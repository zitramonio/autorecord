using Autorecord.Core.Transcription.Jobs;

namespace Autorecord.App.Notifications;

public sealed class TranscriptionNotificationService
{
    private readonly WpfNotificationService _notificationService;

    public TranscriptionNotificationService(WpfNotificationService notificationService)
    {
        _notificationService = notificationService;
    }

    public void ShowFinished(TranscriptionJob job)
    {
        if (job.Status == TranscriptionJobStatus.Completed)
        {
            _notificationService.ShowInfo("Транскрибация завершена", job.InputFilePath);
            return;
        }

        if (job.Status == TranscriptionJobStatus.Failed)
        {
            _notificationService.ShowInfo(
                "Ошибка транскрибации",
                string.IsNullOrWhiteSpace(job.ErrorMessage)
                    ? job.InputFilePath
                    : $"{job.InputFilePath}: {job.ErrorMessage}");
        }
    }
}

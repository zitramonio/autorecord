namespace Autorecord.Core.Transcription.Models;

public enum ModelInstallStatus
{
    NotInstalled = 0,
    Downloading = 1,
    Installed = 2,
    MissingRequiredFiles = 3,
    DownloadUnavailable = 4,
    Error = 5
}

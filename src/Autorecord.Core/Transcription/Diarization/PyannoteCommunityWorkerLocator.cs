namespace Autorecord.Core.Transcription.Diarization;

public static class PyannoteCommunityWorkerLocator
{
    public static string ResolveWorkerPath(string appBaseDirectory, string installedWorkersRoot)
    {
        var bundledWorkerPath = Path.GetFullPath(Path.Combine(appBaseDirectory, "workers", "pyannote-community", "worker.exe"));
        if (File.Exists(bundledWorkerPath))
        {
            return bundledWorkerPath;
        }

        return Path.GetFullPath(Path.Combine(installedWorkersRoot, "PyannoteCommunity", "worker.exe"));
    }
}

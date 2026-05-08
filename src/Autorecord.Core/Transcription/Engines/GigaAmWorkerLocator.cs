namespace Autorecord.Core.Transcription.Engines;

public static class GigaAmWorkerLocator
{
    public static string ResolveWorkerPath(string appBaseDirectory, string installedWorkersRoot)
    {
        var bundledWorkerPath = Path.GetFullPath(Path.Combine(appBaseDirectory, "workers", "gigaam", "worker.exe"));
        if (File.Exists(bundledWorkerPath))
        {
            return bundledWorkerPath;
        }

        return Path.GetFullPath(Path.Combine(installedWorkersRoot, "GigaAM", "worker.exe"));
    }
}

namespace Autorecord.Core.Utilities;

public static class RecordingFileNamer
{
    public static string GetAvailablePath(string outputFolder, DateTimeOffset startedAt)
    {
        Directory.CreateDirectory(outputFolder);
        var baseName = startedAt.DateTime.ToString("dd.MM.yyyy HH.mm");
        var path = Path.Combine(outputFolder, baseName + ".mp3");
        if (!PathIsReserved(path))
        {
            return path;
        }

        for (var index = 2; ; index++)
        {
            var candidate = Path.Combine(outputFolder, $"{baseName} ({index}).mp3");
            if (!PathIsReserved(candidate))
            {
                return candidate;
            }
        }
    }

    private static bool PathIsReserved(string path)
    {
        var directory = Path.GetDirectoryName(path) ?? "";
        var fileName = Path.GetFileNameWithoutExtension(path);
        var temporaryWavPath = Path.Combine(directory, $"{fileName}.recording.wav");
        return File.Exists(path) ||
            File.Exists(temporaryWavPath) ||
            File.Exists($"{path}.tmp") ||
            File.Exists($"{path}.encoding.mp3");
    }
}

namespace Autorecord.Core.Utilities;

public static class RecordingFileNamer
{
    public static string GetAvailablePath(string outputFolder, DateTimeOffset startedAt)
    {
        Directory.CreateDirectory(outputFolder);
        var baseName = startedAt.DateTime.ToString("dd.MM.yyyy HH.mm");
        var path = Path.Combine(outputFolder, baseName + ".wav");
        if (!File.Exists(path))
        {
            return path;
        }

        for (var index = 2; ; index++)
        {
            var candidate = Path.Combine(outputFolder, $"{baseName} ({index}).wav");
            if (!File.Exists(candidate))
            {
                return candidate;
            }
        }
    }
}

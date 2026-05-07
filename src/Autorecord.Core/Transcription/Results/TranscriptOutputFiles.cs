namespace Autorecord.Core.Transcription.Results;

public sealed record TranscriptOutputFiles(
    string? TxtPath,
    string? MarkdownPath,
    string? SrtPath,
    string? JsonPath)
{
    public IReadOnlyList<string> AllPaths
    {
        get
        {
            var paths = new List<string>(4);
            AddIfNotNull(TxtPath);
            AddIfNotNull(MarkdownPath);
            AddIfNotNull(SrtPath);
            AddIfNotNull(JsonPath);
            return paths;

            void AddIfNotNull(string? path)
            {
                if (path is not null)
                {
                    paths.Add(path);
                }
            }
        }
    }
}

namespace Autorecord.Core.Transcription.Models;

public sealed record ModelInstallInfo
{
    public string TargetFolder { get; init; } = "";
    public IReadOnlyList<string> RequiredFiles { get; init; } = [];
}

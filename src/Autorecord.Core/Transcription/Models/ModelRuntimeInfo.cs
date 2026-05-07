namespace Autorecord.Core.Transcription.Models;

public sealed record ModelRuntimeInfo
{
    public int SampleRate { get; init; } = 16000;
    public int Channels { get; init; } = 1;
    public string Device { get; init; } = "cpu";
}

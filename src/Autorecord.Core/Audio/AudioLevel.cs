namespace Autorecord.Core.Audio;

public sealed record AudioLevel(float InputPeak, float OutputPeak)
{
    public bool BothSilent(float threshold) => InputPeak < threshold && OutputPeak < threshold;
}

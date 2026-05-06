using Autorecord.Core.Utilities;

namespace Autorecord.Core.Tests;

public sealed class RecordingFileNamerTests
{
    [Fact]
    public void BuildsNameFromRecordingStartTime()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);

        var result = RecordingFileNamer.GetAvailablePath(dir, new DateTimeOffset(2026, 5, 6, 18, 42, 0, TimeSpan.Zero));

        Assert.Equal(Path.Combine(dir, "06.05.2026 18.42.wav"), result);
    }

    [Fact]
    public void AddsSuffixWhenFileAlreadyExists()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "06.05.2026 18.42.wav"), "");

        var result = RecordingFileNamer.GetAvailablePath(dir, new DateTimeOffset(2026, 5, 6, 18, 42, 0, TimeSpan.Zero));

        Assert.Equal(Path.Combine(dir, "06.05.2026 18.42 (2).wav"), result);
    }
}

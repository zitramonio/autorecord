using Autorecord.App.Dialogs;

namespace Autorecord.Core.Tests;

public sealed class SpeakerCountPromptTests
{
    [Fact]
    public void AutoContinueTimeoutIsTwoMinutes()
    {
        Assert.Equal(TimeSpan.FromMinutes(2), SpeakerCountPrompt.AutoContinueTimeout);
    }
}

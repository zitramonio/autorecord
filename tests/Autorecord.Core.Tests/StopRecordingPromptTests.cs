using Autorecord.App.Dialogs;
using Autorecord.Core.Settings;

namespace Autorecord.Core.Tests;

public sealed class StopRecordingPromptTests
{
    [Fact]
    public void AutoStopTimeoutUsesDefaultSetting()
    {
        Assert.Equal(TimeSpan.FromMinutes(2), StopRecordingPrompt.GetAutoStopTimeout(new AppSettings()));
    }

    [Fact]
    public void AutoStopTimeoutUsesConfiguredMinutes()
    {
        Assert.Equal(
            TimeSpan.FromMinutes(3),
            StopRecordingPrompt.GetAutoStopTimeout(new AppSettings { NoAnswerStopPromptMinutes = 3 }));
    }

    [Theory]
    [InlineData(StopRecordingDialogResponse.Yes, StopRecordingPromptAction.Stop)]
    [InlineData(StopRecordingDialogResponse.Timeout, StopRecordingPromptAction.Stop)]
    [InlineData(StopRecordingDialogResponse.No, StopRecordingPromptAction.Delay)]
    [InlineData(StopRecordingDialogResponse.None, StopRecordingPromptAction.Ignore)]
    public void ResolveActionMapsDialogResponse(
        StopRecordingDialogResponse response,
        StopRecordingPromptAction expectedAction)
    {
        Assert.Equal(expectedAction, StopRecordingPrompt.ResolveAction(response));
    }
}

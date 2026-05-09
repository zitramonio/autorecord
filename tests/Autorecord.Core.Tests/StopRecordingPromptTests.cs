using Autorecord.App.Dialogs;

namespace Autorecord.Core.Tests;

public sealed class StopRecordingPromptTests
{
    [Fact]
    public void AutoStopTimeoutIsTwoMinutes()
    {
        Assert.Equal(TimeSpan.FromMinutes(2), StopRecordingPrompt.AutoStopTimeout);
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

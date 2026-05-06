using Autorecord.Core.Recording;

namespace Autorecord.Core.Tests;

public sealed class StopConfirmationPolicyTests
{
    [Fact]
    public void PromptsAfterContinuousSilence()
    {
        var policy = new StopConfirmationPolicy(TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5));
        var started = new DateTimeOffset(2026, 5, 6, 18, 0, 0, TimeSpan.Zero);

        Assert.False(policy.ShouldPrompt(started, true));
        Assert.True(policy.ShouldPrompt(started.AddMinutes(1), true));
    }

    [Fact]
    public void NoAnswerKeepsRecordingWithoutChangingRetryWindow()
    {
        var policy = new StopConfirmationPolicy(TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5));
        var promptAt = new DateTimeOffset(2026, 5, 6, 18, 1, 0, TimeSpan.Zero);

        policy.RecordNoAnswer(promptAt);

        Assert.True(policy.ShouldPrompt(promptAt.AddSeconds(1), true));
    }

    [Fact]
    public void NoAnswerAsNoWaitsRetryBeforePromptingAgain()
    {
        var policy = new StopConfirmationPolicy(TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5));
        var promptAt = new DateTimeOffset(2026, 5, 6, 18, 1, 0, TimeSpan.Zero);

        policy.RecordNo(promptAt);

        Assert.False(policy.ShouldPrompt(promptAt.AddMinutes(4), true));
        Assert.True(policy.ShouldPrompt(promptAt.AddMinutes(6), true));
    }

    [Fact]
    public void SoundResetsSilenceTimer()
    {
        var policy = new StopConfirmationPolicy(TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5));
        var now = new DateTimeOffset(2026, 5, 6, 18, 1, 0, TimeSpan.Zero);

        Assert.False(policy.ShouldPrompt(now, false));
    }
}

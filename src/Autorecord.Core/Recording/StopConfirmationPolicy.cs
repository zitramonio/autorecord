namespace Autorecord.Core.Recording;

public sealed class StopConfirmationPolicy
{
    private readonly TimeSpan _silenceInterval;
    private readonly TimeSpan _retryInterval;
    private DateTimeOffset? _silenceStartedAt;
    private DateTimeOffset? _snoozedUntil;

    public StopConfirmationPolicy(TimeSpan silenceInterval, TimeSpan retryInterval)
    {
        if (silenceInterval <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(silenceInterval));
        if (retryInterval <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(retryInterval));
        _silenceInterval = silenceInterval;
        _retryInterval = retryInterval;
    }

    public bool ShouldPrompt(DateTimeOffset now, bool bothSourcesSilent)
    {
        if (!bothSourcesSilent)
        {
            _silenceStartedAt = null;
            return false;
        }

        _silenceStartedAt ??= now;

        if (_snoozedUntil is not null && now < _snoozedUntil.Value)
        {
            return false;
        }

        return now - _silenceStartedAt.Value >= _silenceInterval;
    }

    public void RecordNo(DateTimeOffset now)
    {
        _snoozedUntil = now + _retryInterval;
        _silenceStartedAt = null;
    }

    public void RecordNoAnswer(DateTimeOffset now)
    {
        _snoozedUntil = null;
        _silenceStartedAt ??= now - _silenceInterval;
    }
}

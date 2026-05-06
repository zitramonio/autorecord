namespace Autorecord.Core.Recording;

public sealed class StopConfirmationPolicy
{
    private readonly TimeSpan _silenceInterval;
    private readonly TimeSpan _retryInterval;
    private DateTimeOffset? _silenceStartedAt;
    private DateTimeOffset? _snoozedUntil;
    private bool _waitingForAnswer;

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

        if (_waitingForAnswer)
        {
            return false;
        }

        if (_snoozedUntil is not null && now < _snoozedUntil.Value)
        {
            return false;
        }

        if (_snoozedUntil is not null)
        {
            _silenceStartedAt = _snoozedUntil.Value;
            _snoozedUntil = null;
        }

        _silenceStartedAt ??= now;

        if (now - _silenceStartedAt.Value < _silenceInterval)
        {
            return false;
        }

        _waitingForAnswer = true;
        return true;
    }

    public void RecordNo(DateTimeOffset now)
    {
        _snoozedUntil = now + _retryInterval;
        _silenceStartedAt = null;
        _waitingForAnswer = false;
    }

    public void RecordNoAnswer(DateTimeOffset now)
    {
        _snoozedUntil = null;
        _silenceStartedAt ??= now - _silenceInterval;
        _waitingForAnswer = false;
    }
}

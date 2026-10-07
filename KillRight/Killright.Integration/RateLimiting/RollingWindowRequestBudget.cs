using System.Diagnostics;

namespace Killright.Integration.RateLimiting;

public sealed class RollingWindowRequestBudget : IRequestStartLimiter
{
    private readonly int _requestBudget;
    private readonly long _windowTicks;
    private readonly Func<long> _nowTicks;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly object _gate = new();
    private readonly Queue<long> _starts = new();
    private long _pausedUntilTicks;
    private long _totalWaitTicks;

    public RollingWindowRequestBudget(
        int requestBudget,
        TimeSpan window,
        Func<long>? nowTicks = null,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        if (requestBudget < 1)
            throw new ArgumentOutOfRangeException(nameof(requestBudget));

        if (window <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(window));

        _requestBudget = requestBudget;
        _windowTicks = window.Ticks;

        var startTimestamp = Stopwatch.GetTimestamp();
        _nowTicks = nowTicks ?? (() => Stopwatch.GetElapsedTime(startTimestamp).Ticks);
        _delay = delay ?? Task.Delay;
    }

    public long TotalWaitMilliseconds
    {
        get
        {
            lock (_gate)
                return _totalWaitTicks / TimeSpan.TicksPerMillisecond;
        }
    }

    public bool IsPaused
    {
        get
        {
            lock (_gate)
                return _nowTicks() < _pausedUntilTicks;
        }
    }

    public void Pause(TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero)
            return;

        lock (_gate)
            _pausedUntilTicks = Math.Max(_pausedUntilTicks, _nowTicks() + duration.Ticks);
    }

    public async Task WaitAsync(CancellationToken cancellationToken = default)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            TimeSpan wait;

            lock (_gate)
            {
                var now = _nowTicks();

                if (now < _pausedUntilTicks)
                    throw new ZkillRateLimitedException();

                while (_starts.Count > 0 && now - _starts.Peek() >= _windowTicks)
                    _starts.Dequeue();

                if (_starts.Count < _requestBudget)
                {
                    _starts.Enqueue(now);
                    return;
                }

                wait = TimeSpan.FromTicks(_starts.Peek() + _windowTicks - now);
            }

            await _delay(wait, cancellationToken);

            lock (_gate)
                _totalWaitTicks += wait.Ticks;
        }
    }
}

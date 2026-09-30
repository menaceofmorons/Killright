using System.Diagnostics;

namespace Killright.Integration.RateLimiting;

public sealed class RequestStartLimiter : IRequestStartLimiter
{
    private readonly long _intervalTicks;
    private readonly Func<long> _nowTicks;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly object _gate = new();
    private long _nextSlotTicks;
    private long _totalWaitTicks;

    public RequestStartLimiter(
        double requestsPerSecond,
        Func<long>? nowTicks = null,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        if (double.IsNaN(requestsPerSecond) || requestsPerSecond <= 0)
            throw new ArgumentOutOfRangeException(nameof(requestsPerSecond));

        _intervalTicks = Math.Max(1, (long)Math.Ceiling(TimeSpan.TicksPerSecond / requestsPerSecond));

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

    public Task WaitAsync(CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
            return Task.FromCanceled(cancellationToken);

        TimeSpan wait;

        lock (_gate)
        {
            var now = _nowTicks();
            var slot = Math.Max(now, _nextSlotTicks);
            _nextSlotTicks = slot + _intervalTicks;
            _totalWaitTicks += slot - now;
            wait = TimeSpan.FromTicks(slot - now);
        }

        return wait <= TimeSpan.Zero ? Task.CompletedTask : _delay(wait, cancellationToken);
    }
}

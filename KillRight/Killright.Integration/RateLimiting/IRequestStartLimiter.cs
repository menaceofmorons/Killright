namespace Killright.Integration.RateLimiting;

public interface IRequestStartLimiter
{
    long TotalWaitMilliseconds { get; }

    bool IsPaused { get; }

    void Pause(TimeSpan duration);

    Task WaitAsync(CancellationToken cancellationToken = default);
}

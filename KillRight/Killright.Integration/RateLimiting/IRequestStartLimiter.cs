namespace Killright.Integration.RateLimiting;

public interface IRequestStartLimiter
{
    long TotalWaitMilliseconds { get; }

    Task WaitAsync(CancellationToken cancellationToken = default);
}

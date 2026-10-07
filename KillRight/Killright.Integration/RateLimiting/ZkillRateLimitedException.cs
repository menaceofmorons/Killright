namespace Killright.Integration.RateLimiting;

public sealed class ZkillRateLimitedException : Exception
{
    public ZkillRateLimitedException()
        : base("zKillboard requests are paused after a rate-limit refusal.")
    {
    }
}

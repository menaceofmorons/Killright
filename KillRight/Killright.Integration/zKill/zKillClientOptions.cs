namespace Killright.Integration.zKill;

public sealed record zKillClientOptions
{
    public Uri BaseUri { get; init; } = new("https://zkillboard.com/");
    public string UserAgent { get; init; } = "KillRight/1.0 (Developer: T'ral Vsengne)";
    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(3);
    public int RetryCount { get; init; } = 1;
    public TimeSpan RateLimitPause { get; init; } = TimeSpan.FromSeconds(60);
}

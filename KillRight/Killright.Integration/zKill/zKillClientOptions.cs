namespace Killright.Integration.zKill;

public sealed record zKillClientOptions
{
    /// <summary>
    /// Per-request timeout applied to the shared HttpClient in App.xaml.cs.
    /// zKillClient's calls are single-hop and already fail gracefully (catch
    /// blocks return null/empty on any exception, including a timeout), so
    /// this only bounds how long a stalled request can hold up whatever UI
    /// action triggered it, rather than .NET's unconfigured 100-second
    /// default (Session finding, 07 Aug 2026).
    /// </summary>
    public const int RequestTimeoutSeconds = 10;

        public Uri BaseUri { get; init; } = new("https://zkillboard.com/");
    public string UserAgent { get; init; } = "KillRight/1.0 (Developer: T'ral Vsengne)";
}
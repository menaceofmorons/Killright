namespace Killright.Integration.Esi;

public sealed record EsiClientOptions
{
    /// <summary>
    /// Per-request timeout applied to the shared HttpClient in App.xaml.cs.
    /// ResolvePilotAsync chains up to four sequential ESI calls (character
    /// id, character, corporation, alliance), so a worst case of every hop
    /// timing out is a bounded ~40 seconds, not .NET's unconfigured
    /// 100-second default per hop. ESI responses are normally sub-second;
    /// this is a hard ceiling against a stalled request blocking the UI
    /// thread that calls ResolvePilotAsync, not a tuning target for normal
    /// traffic (Session finding, 07 Aug 2026).
    /// </summary>
    public const int RequestTimeoutSeconds = 10;

    public Uri BaseUri { get; init; } = new("https://esi.evetech.net/latest/");
    public string UserAgent { get; init; } = "(menace.of.morons@gmail.com;eve:T'ral Vsengne) KillRight/0.1";
    public string DataSource { get; init; } = "tranquility";
    public string Language { get; init; } = "en";
}

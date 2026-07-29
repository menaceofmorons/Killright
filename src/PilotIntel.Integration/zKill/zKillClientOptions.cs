namespace PilotIntel.Integration.zKill;

public sealed record zKillClientOptions
{
        public Uri BaseUri { get; init; } = new("https://zkillboard.com/");
    public string UserAgent { get; init; } = "PilotIntel/1.0 (Developer: T'ral Vsengne)";
}
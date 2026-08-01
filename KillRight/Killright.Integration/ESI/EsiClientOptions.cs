namespace Killright.Integration.Esi;

public sealed record EsiClientOptions
{
    public Uri BaseUri { get; init; } = new("https://esi.evetech.net/latest/");
    public string UserAgent { get; init; } = "(menace.of.morons@gmail.com;eve:T'ral Vsengne) KillRight/0.1";
    public string DataSource { get; init; } = "tranquility";
    public string Language { get; init; } = "en";
}

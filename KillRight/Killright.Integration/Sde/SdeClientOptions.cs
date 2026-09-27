namespace Killright.Integration.Sde;

public sealed record SdeClientOptions
{
    public const int RequestTimeoutSeconds = 120;

    public string UserAgent { get; init; } = "KillRight/1.0 (Developer: T'ral Vsengne)";

    public string ManifestUrl { get; init; } = "https://developers.eveonline.com/static-data/tranquility/latest.jsonl";

    public string DatasetZipUrl { get; init; } = "https://developers.eveonline.com/static-data/eve-online-static-data-latest-jsonl.zip";
}

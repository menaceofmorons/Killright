namespace Killright.Integration.Sde;

public sealed record SdeClientOptions
{
    public const int RequestTimeoutSeconds = 300;

    public string ManifestUrl { get; init; } = "https://developers.eveonline.com/static-data/tranquility/latest.jsonl";

    public string DatasetZipUrl { get; init; } = "https://developers.eveonline.com/static-data/eve-online-static-data-latest-jsonl.zip";
}

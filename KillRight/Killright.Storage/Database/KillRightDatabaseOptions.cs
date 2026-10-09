namespace Killright.Storage.Database;

public sealed class KillRightDatabaseOptions
{
    public const int DefaultBusyTimeoutSeconds = 5;

    public const int DefaultPageCacheMegabytes = 64;

    public const int DefaultIdleCheckpointSeconds = 30;

    public string DatabasePath { get; init; } = string.Empty;

    public int BusyTimeoutSeconds { get; init; } = DefaultBusyTimeoutSeconds;

    public int PageCacheMegabytes { get; init; } = DefaultPageCacheMegabytes;

    public static int NormalizeBusyTimeoutSeconds(int value)
    {
        return value >= 1 ? value : DefaultBusyTimeoutSeconds;
    }

    public static int NormalizePageCacheMegabytes(int value)
    {
        return value >= 1 ? value : DefaultPageCacheMegabytes;
    }

    public static int NormalizeIdleCheckpointSeconds(int value)
    {
        return value >= 1 ? value : DefaultIdleCheckpointSeconds;
    }
}

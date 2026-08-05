namespace Killright.Integration.zKill.History;

public sealed record ZkillHistoryParallelDownloadOptions(
    int ParallelDownloadWorkers,
    int MaxRequestsPerSecond,
    int ZkillDocumentedMaxRequestsPerSecond)
{
    public const int MinimumParallelDownloadWorkers = 1;
    public const int MaximumParallelDownloadWorkers = 16;
    public const int DefaultParallelDownloadWorkers = 8;

    // Safety margin KillRight subtracts from the configured zKill-documented R2 request-start
    // ceiling so it never approaches the actual zKill limit. This margin is fixed in code; the
    // ceiling itself is configured (config/settings.json) so a future change to zKill's documented
    // limit does not require a rebuild or release — see zKillboard/zKillboard wiki, API (R2Z2).
    public const int SafetyMarginRequestsPerSecond = 2;

    public const int MinimumDocumentedMaxRequestsPerSecond = SafetyMarginRequestsPerSecond + 1;
    public const int MaximumDocumentedMaxRequestsPerSecond = 1000;
    public const int DefaultDocumentedMaxRequestsPerSecond = 15;

    public const int MinimumRequestsPerSecond = 1;

    public static ZkillHistoryParallelDownloadOptions Default { get; } = FromConfiguredValues(
        DefaultParallelDownloadWorkers,
        DefaultDocumentedMaxRequestsPerSecond);

    public static ZkillHistoryParallelDownloadOptions FromConfiguredValues(
        int parallelDownloadWorkers,
        int zkillDocumentedMaxRequestsPerSecond)
    {
        var clampedCeiling = ClampDocumentedMaxRequestsPerSecond(zkillDocumentedMaxRequestsPerSecond);
        var effectiveMaxRequestsPerSecond = clampedCeiling - SafetyMarginRequestsPerSecond;

        return new ZkillHistoryParallelDownloadOptions(
            Clamp(parallelDownloadWorkers, MinimumParallelDownloadWorkers, MaximumParallelDownloadWorkers),
            effectiveMaxRequestsPerSecond,
            clampedCeiling);
    }

    public static int ClampDocumentedMaxRequestsPerSecond(int value)
    {
        return Clamp(value, MinimumDocumentedMaxRequestsPerSecond, MaximumDocumentedMaxRequestsPerSecond);
    }

    private static int Clamp(int value, int minimum, int maximum)
    {
        if (value < minimum)
            return minimum;

        if (value > maximum)
            return maximum;

        return value;
    }
}

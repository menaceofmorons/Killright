namespace Killright.Integration.zKill.History;

public sealed record ZkillHistoryParallelDownloadOptions(
    int ParallelDownloadWorkers,
    int MaxRequestsPerSecond)
{
    public const int MinimumParallelDownloadWorkers = 1;
    public const int MaximumParallelDownloadWorkers = 16;
    public const int DefaultParallelDownloadWorkers = 8;

    // zKillboard-documented R2 request-start ceiling (see zKillboard/zKillboard wiki, API (R2Z2)).
    public const int DocumentedCeilingRequestsPerSecond = 15;

    // Safety margin KillRight subtracts from the documented ceiling so it never approaches the actual zKill limit.
    public const int SafetyMarginRequestsPerSecond = 2;

    public const int MinimumRequestsPerSecond = 1;
    public const int MaximumRequestsPerSecond = DocumentedCeilingRequestsPerSecond - SafetyMarginRequestsPerSecond;
    public const int DefaultMaxRequestsPerSecond = MaximumRequestsPerSecond;

    public static ZkillHistoryParallelDownloadOptions Default { get; } = new(
        DefaultParallelDownloadWorkers,
        DefaultMaxRequestsPerSecond);

    public static ZkillHistoryParallelDownloadOptions FromConfiguredValues(
        int parallelDownloadWorkers,
        int maxRequestsPerSecond)
    {
        return new ZkillHistoryParallelDownloadOptions(
            Clamp(parallelDownloadWorkers, MinimumParallelDownloadWorkers, MaximumParallelDownloadWorkers),
            Clamp(maxRequestsPerSecond, MinimumRequestsPerSecond, MaximumRequestsPerSecond));
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
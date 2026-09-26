#if HISTORIC_RELATIONSHIPS
using Killright.Integration.zKill.History;
using Killright.Storage.GroupHistory.Models;

#endif
using Killright.Shared.Killmails;

namespace Killright.UI.Configuration;

public sealed class ApplicationSettings
{
    public int RecentWindowDays { get; init; } = RecentWindowDefaults.DefaultWindowDays;

    public string? BackupFolder { get; init; }

    public int BackupRotationCount { get; init; } = KillmailBackupDefaults.DefaultRotationCount;

    public bool AlphaReleaseSchemaLocked { get; init; }

    public IReadOnlyList<ThreatBandSetting> ThreatBands { get; init; } = ThreatBandSetting.Defaults;

    public long NpcCorporationIdThreshold { get; init; } = 1_005_000;

#if HISTORIC_RELATIONSHIPS
    public GroupHistoryApplicationSettings GroupHistory { get; init; } = new();
#endif
}

public sealed class ThreatBandSetting
{
    public string Name { get; init; } = string.Empty;

    public int MinimumScore { get; init; }

    public int MaximumScore { get; init; }

    public static IReadOnlyList<ThreatBandSetting> Defaults { get; } = new[]
    {
        new ThreatBandSetting { Name = "Low", MinimumScore = 1, MaximumScore = 20 },
        new ThreatBandSetting { Name = "Medium", MinimumScore = 21, MaximumScore = 40 },
        new ThreatBandSetting { Name = "High", MinimumScore = 41, MaximumScore = 60 },
        new ThreatBandSetting { Name = "Very High", MinimumScore = 61, MaximumScore = 80 },
        new ThreatBandSetting { Name = "Extreme", MinimumScore = 81, MaximumScore = 100 }
    };

    public static IReadOnlyList<ThreatBandSetting> ValidateOrDefault(IReadOnlyList<ThreatBandSetting>? bands)
    {
        if (bands is null || bands.Count == 0)
            return Defaults;

        var ordered = bands.OrderBy(band => band.MinimumScore).ToList();
        var expectedMinimum = 1;

        foreach (var band in ordered)
        {
            if (band.MinimumScore != expectedMinimum || band.MaximumScore < band.MinimumScore)
                return Defaults;

            expectedMinimum = band.MaximumScore + 1;
        }

        return expectedMinimum == 101 ? ordered : Defaults;
    }
}

#if HISTORIC_RELATIONSHIPS
public sealed class GroupHistoryApplicationSettings
{
    public int ImportBatchSize { get; init; } = GroupHistoryImportBatchOptions.DefaultBatchSize;

    public int ParallelDownloadWorkers { get; init; } = ZkillHistoryParallelDownloadOptions.DefaultParallelDownloadWorkers;

    public int ZkillDocumentedMaxRequestsPerSecond { get; init; } = ZkillHistoryParallelDownloadOptions.DefaultDocumentedMaxRequestsPerSecond;

    public GroupHistoryImportBatchOptions ToBatchOptions()
    {
        return GroupHistoryImportBatchOptions.FromSingleBatchSize(ImportBatchSize);
    }

    public ZkillHistoryParallelDownloadOptions ToParallelDownloadOptions()
    {
        return ZkillHistoryParallelDownloadOptions.FromConfiguredValues(
            ParallelDownloadWorkers,
            ZkillDocumentedMaxRequestsPerSecond);
    }
}
#endif

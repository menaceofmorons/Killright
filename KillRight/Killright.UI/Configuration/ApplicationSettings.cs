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

    public IReadOnlyList<RelationshipConfidenceBandSetting> RelationshipConfidenceBands { get; init; } = RelationshipConfidenceBandSetting.Defaults;

    public double HighlightOpacity { get; init; } = 0.20;

    public int QualificationFleetThreshold { get; init; } = 11;

    public SdeApplicationSettings Sde { get; init; } = new();

    public ThreatSettings Threat { get; init; } = new();

    public GroupDetectionSettings GroupDetection { get; init; } = new();

    public StyleSettings Style { get; init; } = new();

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

public sealed class RelationshipConfidenceBandSetting
{
    public string Name { get; init; } = string.Empty;

    public int MinimumScore { get; init; }

    public int MaximumScore { get; init; }

    public static IReadOnlyList<RelationshipConfidenceBandSetting> Defaults { get; } = new[]
    {
        new RelationshipConfidenceBandSetting { Name = "Low", MinimumScore = 1, MaximumScore = 25 },
        new RelationshipConfidenceBandSetting { Name = "Medium", MinimumScore = 26, MaximumScore = 75 },
        new RelationshipConfidenceBandSetting { Name = "High", MinimumScore = 76, MaximumScore = 100 }
    };

    public static IReadOnlyList<RelationshipConfidenceBandSetting> ValidateOrDefault(IReadOnlyList<RelationshipConfidenceBandSetting>? bands)
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

public sealed class SdeApplicationSettings
{
    public string ManifestUrl { get; init; } = "https://developers.eveonline.com/static-data/tranquility/latest.jsonl";

    public string DatasetZipUrl { get; init; } = "https://developers.eveonline.com/static-data/eve-online-static-data-latest-jsonl.zip";

    public int CheckIntervalHours { get; init; } = 24;
}

public sealed class StyleSettings
{
    public double BlobMinimumAverageAttackers { get; init; } = 5;

    public double FleetMinimumAverageAttackers { get; init; } = 11;
}

public sealed class ThreatSettings
{
    public string Version { get; init; } = "2.0";

    public string LastUpdated { get; init; } = string.Empty;

    public ThreatComponentWeightsSettings ComponentWeights { get; init; } = new();

    public ThreatHistoricalCapabilitySettings HistoricalCapability { get; init; } = new();

    public ThreatSurvivabilitySettings Survivability { get; init; } = new();

    public ThreatLossQualitySettings LossQuality { get; init; } = new();

    public ThreatRecentActivitySettings RecentActivity { get; init; } = new();

    public ThreatSecurityStatusSettings SecurityStatus { get; init; } = new();
}

public sealed class ThreatComponentWeightsSettings
{
    public ThreatComponentWeightSetting HistoricalCapability { get; init; } = new() { MaximumScore = 40 };

    public ThreatComponentWeightSetting Survivability { get; init; } = new() { MaximumScore = 25 };

    public ThreatComponentWeightSetting LossQuality { get; init; } = new() { MaximumScore = 20 };

    public ThreatComponentWeightSetting RecentActivity { get; init; } = new() { MaximumScore = 10 };

    public ThreatComponentWeightSetting SecurityStatus { get; init; } = new() { MaximumScore = 5 };
}

public sealed class ThreatComponentWeightSetting
{
    public int MaximumScore { get; init; }
}

public sealed class ThreatHistoricalCapabilitySettings
{
    public IReadOnlyList<ThreatKillVolumeBandSetting> KillVolumeBands { get; init; } = Array.Empty<ThreatKillVolumeBandSetting>();

    public IReadOnlyList<ThreatSoloKillBandSetting> SoloKillBands { get; init; } = Array.Empty<ThreatSoloKillBandSetting>();

    public ThreatSoloRatioRuleSetting SoloRatio { get; init; } = new();

    public IReadOnlyList<ThreatStyleModifierSetting> StyleModifiers { get; init; } = Array.Empty<ThreatStyleModifierSetting>();
}

public sealed class ThreatKillVolumeBandSetting
{
    public int MaximumKills { get; init; }

    public int Score { get; init; }
}

public sealed class ThreatSoloKillBandSetting
{
    public int MaximumSoloKills { get; init; }

    public int Score { get; init; }
}

public sealed class ThreatSoloRatioRuleSetting
{
    public double MinimumRatio { get; init; }

    public int Score { get; init; }
}

public sealed class ThreatStyleModifierSetting
{
    public string Style { get; init; } = string.Empty;

    public int Score { get; init; }
}

public sealed class ThreatSurvivabilitySettings
{
    public IReadOnlyList<ThreatRatioBandSetting> Ratios { get; init; } = Array.Empty<ThreatRatioBandSetting>();

    public IReadOnlyList<ThreatNoLossBandSetting> NoLossBands { get; init; } = Array.Empty<ThreatNoLossBandSetting>();
}

public sealed class ThreatRatioBandSetting
{
    public double MaximumRatio { get; init; }

    public int Score { get; init; }
}

public sealed class ThreatNoLossBandSetting
{
    public int MinimumKills { get; init; }

    public int Score { get; init; }
}

public sealed class ThreatLossQualitySettings
{
    public IReadOnlyList<ThreatLossQualityStyleSetting> Styles { get; init; } = Array.Empty<ThreatLossQualityStyleSetting>();

    public ThreatLossQualityDefaultStyleSetting DefaultStyle { get; init; } = new();

    public int NoLossesWithKillsScore { get; init; }
}

public sealed class ThreatLossQualityStyleSetting
{
    public string Style { get; init; } = string.Empty;

    public IReadOnlyList<ThreatLossQualityBandSetting> Bands { get; init; } = Array.Empty<ThreatLossQualityBandSetting>();
}

public sealed class ThreatLossQualityDefaultStyleSetting
{
    public IReadOnlyList<ThreatLossQualityBandSetting> Bands { get; init; } = Array.Empty<ThreatLossQualityBandSetting>();
}

public sealed class ThreatLossQualityBandSetting
{
    public double MinimumSoloLossRatio { get; init; }

    public int Score { get; init; }
}

public sealed class ThreatRecentActivitySettings
{
    public IReadOnlyList<ThreatRecentActivityPointSetting> Points { get; init; } = Array.Empty<ThreatRecentActivityPointSetting>();
}

public sealed class ThreatRecentActivityPointSetting
{
    public double DailyRate { get; init; }

    public double Score { get; init; }
}

public sealed class ThreatSecurityStatusSettings
{
    public IReadOnlyList<ThreatSecurityStatusBandSetting> Bands { get; init; } = Array.Empty<ThreatSecurityStatusBandSetting>();
}

public sealed class ThreatSecurityStatusBandSetting
{
    public double MinimumSecurityStatus { get; init; }

    public int Score { get; init; }
}

public sealed class GroupDetectionSettings
{
    public long MinimumSharedEvents { get; init; }

    public int StrengthStep { get; init; }

    public IReadOnlyList<GroupDetectionGangSizeWeightSetting> GangSizeWeights { get; init; } = Array.Empty<GroupDetectionGangSizeWeightSetting>();

    public GroupDetectionSampleFactorSetting SampleFactor { get; init; } = new();

    public int SplitBonus { get; init; }

    public double ChainDiscount { get; init; }

    public GroupDetectionIntermediaryBonusSetting IntermediaryBonus { get; init; } = new();
}

public sealed class GroupDetectionGangSizeWeightSetting
{
    public long MaximumGangSize { get; init; }

    public double Weight { get; init; }
}

public sealed class GroupDetectionSampleFactorSetting
{
    public double Minimum { get; init; }

    public double Maximum { get; init; }

    public long SaturatesAtCountedSharedKills { get; init; }
}

public sealed class GroupDetectionIntermediaryBonusSetting
{
    public int PerAdditional { get; init; }

    public int Maximum { get; init; }
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

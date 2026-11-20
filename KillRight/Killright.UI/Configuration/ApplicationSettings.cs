using Killright.Shared.Killmails;
using Killright.Storage.Database;

namespace Killright.UI.Configuration;

public sealed class ApplicationSettings
{
    public int RecentWindowDays { get; init; } = RecentWindowDefaults.DefaultWindowDays;

    public string? BackupFolder { get; init; }

    public int BackupRotationCount { get; init; } = KillmailBackupDefaults.DefaultRotationCount;

    public IReadOnlyList<ThreatBandSetting> ThreatBands { get; init; } = ThreatBandSetting.Defaults;

    public IReadOnlyList<RelationshipConfidenceBandSetting> RelationshipConfidenceBands { get; init; } = RelationshipConfidenceBandSetting.Defaults;

    public double HighlightOpacity { get; init; } = 0.20;

    public int QualificationFleetThreshold { get; init; } = 11;

    public SdeApplicationSettings Sde { get; init; } = new();

    public ThreatSettings Threat { get; init; } = new();

    public GroupDetectionSettings GroupDetection { get; init; } = new();

    public StyleSettings Style { get; init; } = new();

    public TimingSettings Timing { get; init; } = new();

    public NetworkSettings Network { get; init; } = new();

    public DatabaseSettings Database { get; init; } = new();

    public WindowSettings Window { get; init; } = new();
}

public sealed record WindowSettings
{
    public F1WindowSettings F1 { get; init; } = new();

    internal WindowSettings Normalized(Action<string> log)
    {
        return new WindowSettings { F1 = (F1 ?? new F1WindowSettings()).Normalized(log) };
    }
}

public sealed record F1WindowSettings
{
    public const double DefaultWidth = 425;

    public const double DefaultHeight = 424;

    public const double MinimumSize = 60;

    public double Width { get; init; } = DefaultWidth;

    public double Height { get; init; } = DefaultHeight;

    internal F1WindowSettings Normalized(Action<string> log)
    {
        return new F1WindowSettings
        {
            Width = SettingsAdjustment.SizeOrDefault("window.f1.width", Width, MinimumSize, DefaultWidth, log),
            Height = SettingsAdjustment.SizeOrDefault("window.f1.height", Height, MinimumSize, DefaultHeight, log)
        };
    }
}

public sealed class ThreatBandSetting
{
    public int MinimumScore { get; init; }

    public int MaximumScore { get; init; }

    public static IReadOnlyList<ThreatBandSetting> Defaults { get; } = new[]
    {
        new ThreatBandSetting { MinimumScore = 1, MaximumScore = 20 },
        new ThreatBandSetting { MinimumScore = 21, MaximumScore = 40 },
        new ThreatBandSetting { MinimumScore = 41, MaximumScore = 60 },
        new ThreatBandSetting { MinimumScore = 61, MaximumScore = 80 },
        new ThreatBandSetting { MinimumScore = 81, MaximumScore = 100 }
    };

    public static IReadOnlyList<ThreatBandSetting> ValidateOrDefault(IReadOnlyList<ThreatBandSetting>? bands, Action<string>? log = null)
    {
        var problem = FirstProblem(bands);

        if (problem is not null)
        {
            SettingsAdjustment.LogBandsReplaced(
                "threatBands",
                bands?.Select(band => (band.MinimumScore, band.MaximumScore)),
                problem,
                log ?? (_ => { }));

            return Defaults;
        }

        return bands!.OrderBy(band => band.MinimumScore).ToList();
    }

    private static string? FirstProblem(IReadOnlyList<ThreatBandSetting>? bands)
    {
        if (bands is null)
            return "null";

        if (bands.Count == 0)
            return "empty";

        var expectedMinimum = 1;

        foreach (var band in bands.OrderBy(band => band.MinimumScore))
        {
            if (band.MinimumScore != expectedMinimum || band.MaximumScore < band.MinimumScore)
                return "gapped, overlapping or not starting at 1";

            expectedMinimum = band.MaximumScore + 1;
        }

        return expectedMinimum == 101 ? null : "not covering 1 to 100";
    }
}

public sealed class RelationshipConfidenceBandSetting
{
    public int MinimumScore { get; init; }

    public int MaximumScore { get; init; }

    public static IReadOnlyList<RelationshipConfidenceBandSetting> Defaults { get; } = new[]
    {
        new RelationshipConfidenceBandSetting { MinimumScore = 1, MaximumScore = 25 },
        new RelationshipConfidenceBandSetting { MinimumScore = 26, MaximumScore = 75 },
        new RelationshipConfidenceBandSetting { MinimumScore = 76, MaximumScore = 100 }
    };

    public static IReadOnlyList<RelationshipConfidenceBandSetting> ValidateOrDefault(IReadOnlyList<RelationshipConfidenceBandSetting>? bands, Action<string>? log = null)
    {
        var problem = FirstProblem(bands);

        if (problem is not null)
        {
            SettingsAdjustment.LogBandsReplaced(
                "relationshipConfidenceBands",
                bands?.Select(band => (band.MinimumScore, band.MaximumScore)),
                problem,
                log ?? (_ => { }));

            return Defaults;
        }

        return bands!.OrderBy(band => band.MinimumScore).ToList();
    }

    private static string? FirstProblem(IReadOnlyList<RelationshipConfidenceBandSetting>? bands)
    {
        if (bands is null)
            return "null";

        if (bands.Count == 0)
            return "empty";

        var expectedMinimum = 1;

        foreach (var band in bands.OrderBy(band => band.MinimumScore))
        {
            if (band.MinimumScore != expectedMinimum || band.MaximumScore < band.MinimumScore)
                return "gapped, overlapping or not starting at 1";

            expectedMinimum = band.MaximumScore + 1;
        }

        return expectedMinimum == 101 ? null : "not covering 1 to 100";
    }
}

public sealed class SdeApplicationSettings
{
    public string ManifestUrl { get; init; } = "https://developers.eveonline.com/static-data/tranquility/latest.jsonl";

    public string DatasetZipUrl { get; init; } = "https://developers.eveonline.com/static-data/eve-online-static-data-latest-jsonl.zip";

    public int CheckIntervalHours { get; init; } = 24;
}

public sealed record NetworkSettings
{
    public const int DefaultEsiMaxConcurrency = 8;

    public int EsiMaxConcurrency { get; init; } = DefaultEsiMaxConcurrency;

    public ZkillSettings Zkill { get; init; } = new();

    internal NetworkSettings Normalized(Action<string> log)
    {
        return new NetworkSettings
        {
            EsiMaxConcurrency = SettingsAdjustment.Limit("network.esiMaxConcurrency", EsiMaxConcurrency, 1, null, log),
            Zkill = (Zkill ?? new ZkillSettings()).Normalized(log)
        };
    }
}

public sealed record ZkillSettings
{
    public const int DefaultRequestBudget = 550;

    public const int MaximumRequestBudget = 600;

    public const int DefaultBudgetWindowSeconds = 60;

    public const int DefaultMaxConcurrency = 50;

    public const int DefaultRequestTimeoutSeconds = 3;

    public const int DefaultRetryCount = 1;

    public const int DefaultRateLimitPauseSeconds = 60;

    public int RequestBudget { get; init; } = DefaultRequestBudget;

    public int BudgetWindowSeconds { get; init; } = DefaultBudgetWindowSeconds;

    public int MaxConcurrency { get; init; } = DefaultMaxConcurrency;

    public int RequestTimeoutSeconds { get; init; } = DefaultRequestTimeoutSeconds;

    public int RetryCount { get; init; } = DefaultRetryCount;

    public int RateLimitPauseSeconds { get; init; } = DefaultRateLimitPauseSeconds;

    internal ZkillSettings Normalized(Action<string> log)
    {
        return new ZkillSettings
        {
            RequestBudget = SettingsAdjustment.Limit("network.zkill.requestBudget", RequestBudget, 1, MaximumRequestBudget, log),
            BudgetWindowSeconds = SettingsAdjustment.Limit("network.zkill.budgetWindowSeconds", BudgetWindowSeconds, 1, null, log),
            MaxConcurrency = SettingsAdjustment.Limit("network.zkill.maxConcurrency", MaxConcurrency, 1, null, log),
            RequestTimeoutSeconds = SettingsAdjustment.Limit("network.zkill.requestTimeoutSeconds", RequestTimeoutSeconds, 1, null, log),
            RetryCount = RetryCount,
            RateLimitPauseSeconds = SettingsAdjustment.Limit("network.zkill.rateLimitPauseSeconds", RateLimitPauseSeconds, 1, null, log)
        };
    }
}

public sealed record DatabaseSettings
{
    public int BusyTimeoutSeconds { get; init; } = KillRightDatabaseOptions.DefaultBusyTimeoutSeconds;

    public int PageCacheMegabytes { get; init; } = KillRightDatabaseOptions.DefaultPageCacheMegabytes;

    public int IdleCheckpointSeconds { get; init; } = KillRightDatabaseOptions.DefaultIdleCheckpointSeconds;

    internal DatabaseSettings Normalized(Action<string> log)
    {
        return new DatabaseSettings
        {
            BusyTimeoutSeconds = SettingsAdjustment.Limit("database.busyTimeoutSeconds", BusyTimeoutSeconds, 1, null, log),
            PageCacheMegabytes = SettingsAdjustment.Limit("database.pageCacheMegabytes", PageCacheMegabytes, 1, null, log),
            IdleCheckpointSeconds = SettingsAdjustment.Limit("database.idleCheckpointSeconds", IdleCheckpointSeconds, 1, null, log)
        };
    }
}

public sealed class TimingSettings
{
    public bool Enabled { get; init; }
}

public sealed class StyleSettings
{
    public double BlobMinimumAverageAttackers { get; init; } = 5;

    public double FleetMinimumAverageAttackers { get; init; } = 11;

    public double PodderMinimumSharePercent { get; init; } = 35;

    public int PodderMinimumKillCount { get; init; } = 5;
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

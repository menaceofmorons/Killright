using Killright.Shared.Killmails;
using Killright.UI.Configuration;
using Xunit;

namespace Killright.UI.Tests.Configuration;

public sealed class ApplicationSettingsLoaderTests
{
    [Fact]
    public void Load_RealSettingsFile_ReadsEverySectionWithoutDiscardingValues()
    {
        var settings = ApplicationSettingsLoader.Load(ApplicationSettingsLoader.GetDefaultSettingsPath()).Settings!;

        Assert.Equal(14, settings.RecentWindowDays);
        Assert.Equal(5, settings.BackupRotationCount);
        Assert.Equal(5, settings.ThreatBands.Count);
        Assert.Equal(3, settings.RelationshipConfidenceBands.Count);
        Assert.Equal(0.2, settings.HighlightOpacity);
        Assert.Equal(40, settings.Threat.ComponentWeights.HistoricalCapability.MaximumScore);
        Assert.Equal(2, settings.GroupDetection.MinimumSharedEvents);
        Assert.Equal(5, settings.Style.BlobMinimumAverageAttackers);
        Assert.Equal(11, settings.Style.FleetMinimumAverageAttackers);
    }

    [Fact]
    public void Load_RealSettingsFile_TimingIsDisabled()
    {
        var settings = ApplicationSettingsLoader.Load(ApplicationSettingsLoader.GetDefaultSettingsPath()).Settings!;

        Assert.False(settings.Timing.Enabled);
    }

    [Fact]
    public void Load_SectionAbsent_TimingDefaultsToDisabled()
    {
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"killright-settings-{Guid.NewGuid():N}.json");
        System.IO.File.WriteAllText(path, "{ \"recentWindowDays\": 14 }");

        try
        {
            Assert.False(ApplicationSettingsLoader.Load(path).Settings!.Timing.Enabled);
        }
        finally
        {
            System.IO.File.Delete(path);
        }
    }

    [Fact]
    public void Load_TimingEnabledTrue_IsRead()
    {
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"killright-settings-{Guid.NewGuid():N}.json");
        System.IO.File.WriteAllText(path, "{ \"timing\": { \"enabled\": true }, \"qualificationFleetThreshold\": 11 }");

        try
        {
            Assert.True(ApplicationSettingsLoader.Load(path).Settings!.Timing.Enabled);
        }
        finally
        {
            System.IO.File.Delete(path);
        }
    }

    [Fact]
    public void Load_MissingFile_FailsWithNoSettings()
    {
        var result = ApplicationSettingsLoader.Load(@"C:\does-not-exist\settings.json");

        Assert.False(result.Succeeded);
        Assert.Null(result.Settings);
        Assert.Contains("not found", result.Failure);
    }

    [Fact]
    public void Load_UnreadableFile_FailsWithNoSettings()
    {
        var path = TempPath();
        System.IO.File.WriteAllText(path, "{ \"recentWindowDays\": 14 }");

        try
        {
            using var held = new System.IO.FileStream(path, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.None);

            var result = ApplicationSettingsLoader.Load(path);

            Assert.False(result.Succeeded);
            Assert.Null(result.Settings);
            Assert.Contains("could not be read", result.Failure);
        }
        finally
        {
            System.IO.File.Delete(path);
        }
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("")]
    [InlineData("{ \"recentWindowDays\": \"many\" }")]
    public void Load_InvalidJson_FailsWithNoSettings(string json)
    {
        var result = LoadResult(json);

        Assert.False(result.Succeeded);
        Assert.Null(result.Settings);
        Assert.Contains("not valid", result.Failure);
    }

    [Fact]
    public void Load_QualificationThresholdBelowMinimum_IsSetToMinimumAndLogged()
    {
        var log = new List<string>();

        var result = LoadResult("{ \"qualificationFleetThreshold\": 2 }", log);

        Assert.True(result.Succeeded);
        Assert.Equal(3, result.Settings!.QualificationFleetThreshold);
        Assert.Contains("Setting 'qualificationFleetThreshold' read as 2; using 3 (below the minimum of 3).", log);
    }

    [Fact]
    public void Load_QualificationThresholdMissing_TakesDefaultElevenAndLogs()
    {
        var log = new List<string>();

        var result = LoadResult("{ }", log);

        Assert.Equal(11, result.Settings!.QualificationFleetThreshold);
        Assert.Contains("Setting 'qualificationFleetThreshold' read as missing; using the default 11.", log);
    }

    [Fact]
    public void Load_RealSettingsFile_LogsNoAdjustment()
    {
        var log = new List<string>();

        ApplicationSettingsLoader.Load(ApplicationSettingsLoader.GetDefaultSettingsPath(), log.Add);

        Assert.Empty(log);
    }

    [Fact]
    public void Load_BandsMissing_TakeBuiltInBandsAndLog()
    {
        var log = new List<string>();

        var settings = LoadResult("{ }", log).Settings!;

        Assert.Equal(ThreatBandSetting.Defaults.Select(band => (band.MinimumScore, band.MaximumScore)), settings.ThreatBands.Select(band => (band.MinimumScore, band.MaximumScore)));
        Assert.Equal(RelationshipConfidenceBandSetting.Defaults.Select(band => (band.MinimumScore, band.MaximumScore)), settings.RelationshipConfidenceBands.Select(band => (band.MinimumScore, band.MaximumScore)));
        Assert.Contains("Setting 'threatBands' read as missing; using the built-in bands.", log);
        Assert.Contains("Setting 'relationshipConfidenceBands' read as missing; using the built-in bands.", log);
    }

    [Theory]
    [InlineData("null", "null", "null")]
    [InlineData("[]", "[]", "empty")]
    [InlineData("[{ \"minimumScore\": 1, \"maximumScore\": 20 }, { \"minimumScore\": 30, \"maximumScore\": 100 }]", "[1-20, 30-100]", "gapped, overlapping or not starting at 1")]
    [InlineData("[{ \"minimumScore\": 1, \"maximumScore\": 60 }, { \"minimumScore\": 50, \"maximumScore\": 100 }]", "[1-60, 50-100]", "gapped, overlapping or not starting at 1")]
    [InlineData("[{ \"minimumScore\": 1, \"maximumScore\": 50 }]", "[1-50]", "not covering 1 to 100")]
    public void Load_InvalidBands_TakeBuiltInBandsAndLog(string bands, string described, string reason)
    {
        var log = new List<string>();

        var result = LoadResult($"{{ \"threatBands\": {bands}, \"relationshipConfidenceBands\": {bands} }}", log);

        Assert.True(result.Succeeded);
        Assert.Same(ThreatBandSetting.Defaults, result.Settings!.ThreatBands);
        Assert.Same(RelationshipConfidenceBandSetting.Defaults, result.Settings.RelationshipConfidenceBands);
        Assert.Contains($"Setting 'threatBands' read as {described}; using the built-in bands ({reason}).", log);
        Assert.Contains($"Setting 'relationshipConfidenceBands' read as {described}; using the built-in bands ({reason}).", log);
    }

    [Fact]
    public void Load_RetryCountZero_Fails()
    {
        var result = LoadResult("{ \"network\": { \"zkill\": { \"retryCount\": 0 } } }");

        Assert.False(result.Succeeded);
        Assert.Contains("network.zkill.retryCount", result.Failure);
    }

    [Fact]
    public void Load_ValidFile_Succeeds()
    {
        var result = LoadResult("{ \"recentWindowDays\": 21 }");

        Assert.True(result.Succeeded);
        Assert.Null(result.Failure);
        Assert.Equal(21, result.Settings!.RecentWindowDays);
    }

    private static string TempPath() => System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"killright-settings-{Guid.NewGuid():N}.json");

    private static SettingsLoadResult LoadResult(string json, List<string>? log = null)
    {
        var path = TempPath();
        System.IO.File.WriteAllText(path, json);

        try
        {
            return ApplicationSettingsLoader.Load(path, log is null ? null : log.Add);
        }
        finally
        {
            System.IO.File.Delete(path);
        }
    }

    [Fact]
    public void Load_RetiredSectionPresent_IgnoredAndOtherValuesIntact()
    {
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"killright-settings-{Guid.NewGuid():N}.json");
        System.IO.File.WriteAllText(path, """
        {
          "recentWindowDays": 21,
          "backupRotationCount": 7,
          "highlightOpacity": 0.35,
          "groupHistory": { "importBatchSize": 200, "parallelDownloadWorkers": 12, "zkillDocumentedMaxRequestsPerSecond": 8 },
          "style": { "blobMinimumAverageAttackers": 6, "fleetMinimumAverageAttackers": 12 }
        }
        """);

        try
        {
            var settings = ApplicationSettingsLoader.Load(path).Settings!;

            Assert.Equal(21, settings.RecentWindowDays);
            Assert.Equal(7, settings.BackupRotationCount);
            Assert.Equal(0.35, settings.HighlightOpacity);
            Assert.Equal(6, settings.Style.BlobMinimumAverageAttackers);
            Assert.Equal(12, settings.Style.FleetMinimumAverageAttackers);
        }
        finally
        {
            System.IO.File.Delete(path);
        }
    }

    [Theory]
    [InlineData("sde")]
    [InlineData("threat")]
    [InlineData("groupDetection")]
    [InlineData("style")]
    [InlineData("timing")]
    public void Load_ExplicitNullSection_FailsWithNoSettings(string section)
    {
        var path = TempPath();
        System.IO.File.WriteAllText(path, $"{{ \"{section}\": null }}");

        try
        {
            var result = ApplicationSettingsLoader.Load(path);

            Assert.False(result.Succeeded);
            Assert.Null(result.Settings);
            Assert.Contains(section, result.Failure);
        }
        finally
        {
            System.IO.File.Delete(path);
        }
    }

    [Theory]
    [InlineData("network", "network.esiMaxConcurrency")]
    [InlineData("database", "database.busyTimeoutSeconds")]
    public void Load_ExplicitNullNetworkOrDatabase_UsesDefaultsAndLogs(string section, string loggedKey)
    {
        var path = TempPath();
        System.IO.File.WriteAllText(path, $"{{ \"{section}\": null }}");
        var log = new List<string>();

        try
        {
            var result = ApplicationSettingsLoader.Load(path, log.Add);

            Assert.True(result.Succeeded);
            Assert.NotNull(result.Settings!.Network);
            Assert.NotNull(result.Settings.Database);
            Assert.Contains(log, line => line.Contains(loggedKey));
        }
        finally
        {
            System.IO.File.Delete(path);
        }
    }

    [Fact]
    public void Load_UnsupportedValueShape_FailsWithNoSettings()
    {
        var path = TempPath();
        System.IO.File.WriteAllText(path, "{ \"recentWindowDays\": [1, 2] }");

        try
        {
            var result = ApplicationSettingsLoader.Load(path);

            Assert.False(result.Succeeded);
            Assert.Null(result.Settings);
        }
        finally
        {
            System.IO.File.Delete(path);
        }
    }
}

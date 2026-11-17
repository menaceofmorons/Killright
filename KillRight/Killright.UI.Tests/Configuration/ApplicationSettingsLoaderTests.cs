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
    public void Load_QualificationThresholdBelowMinimum_Fails()
    {
        var result = LoadResult("{ \"qualificationFleetThreshold\": 2 }");

        Assert.False(result.Succeeded);
        Assert.Contains("qualificationFleetThreshold", result.Failure);
    }

    [Fact]
    public void Load_RetryCountZero_Fails()
    {
        var result = LoadResult("{ \"network\": { \"zkill\": { \"retryCount\": 0 } } }");

        Assert.False(result.Succeeded);
        Assert.Contains("network.zkill.retryCount", result.Failure);
    }

    [Fact]
    public void Load_SeveralInvalidValues_ReportsEach()
    {
        var result = LoadResult("{ \"qualificationFleetThreshold\": 0, \"database\": { \"busyTimeoutSeconds\": 0 } }");

        Assert.False(result.Succeeded);
        Assert.Contains("qualificationFleetThreshold", result.Failure);
        Assert.Contains("database.busyTimeoutSeconds", result.Failure);
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

    private static SettingsLoadResult LoadResult(string json)
    {
        var path = TempPath();
        System.IO.File.WriteAllText(path, json);

        try
        {
            return ApplicationSettingsLoader.Load(path);
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
}

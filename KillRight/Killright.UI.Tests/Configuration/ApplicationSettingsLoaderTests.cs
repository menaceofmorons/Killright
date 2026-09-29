using Killright.Shared.Killmails;
using Killright.UI.Configuration;
using Xunit;

namespace Killright.UI.Tests.Configuration;

public sealed class ApplicationSettingsLoaderTests
{
    [Fact]
    public void LoadOrDefault_RealSettingsFile_ReadsEverySectionWithoutDiscardingValues()
    {
        var settings = ApplicationSettingsLoader.LoadOrDefault(ApplicationSettingsLoader.GetDefaultSettingsPath());

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
    public void LoadOrDefault_RealSettingsFile_TimingIsDisabled()
    {
        var settings = ApplicationSettingsLoader.LoadOrDefault(ApplicationSettingsLoader.GetDefaultSettingsPath());

        Assert.False(settings.Timing.Enabled);
    }

    [Fact]
    public void LoadOrDefault_SectionAbsent_TimingDefaultsToDisabled()
    {
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"killright-settings-{Guid.NewGuid():N}.json");
        System.IO.File.WriteAllText(path, "{ \"recentWindowDays\": 14 }");

        try
        {
            Assert.False(ApplicationSettingsLoader.LoadOrDefault(path).Timing.Enabled);
        }
        finally
        {
            System.IO.File.Delete(path);
        }
    }

    [Fact]
    public void LoadOrDefault_TimingEnabledTrue_IsRead()
    {
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"killright-settings-{Guid.NewGuid():N}.json");
        System.IO.File.WriteAllText(path, "{ \"timing\": { \"enabled\": true }, \"qualificationFleetThreshold\": 1 }");

        try
        {
            Assert.True(ApplicationSettingsLoader.LoadOrDefault(path).Timing.Enabled);
        }
        finally
        {
            System.IO.File.Delete(path);
        }
    }

    [Fact]
    public void LoadOrDefault_MissingFile_ReturnsDefaults()
    {
        var settings = ApplicationSettingsLoader.LoadOrDefault(@"C:\does-not-exist\settings.json");

        Assert.Equal(RecentWindowDefaults.DefaultWindowDays, settings.RecentWindowDays);
        Assert.Equal(5, settings.Style.BlobMinimumAverageAttackers);
        Assert.Equal(11, settings.Style.FleetMinimumAverageAttackers);
    }

#if HISTORIC_RELATIONSHIPS
    [Fact]
    public void LoadOrDefault_HistoricRelationshipsBuild_PreservesEveryPropertyWhileNormalizingGroupHistory()
    {
        var path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"killright-settings-normalize-{System.Guid.NewGuid():N}.json");

        System.IO.File.WriteAllText(path, """
        {
          "recentWindowDays": 21,
          "backupRotationCount": 7,
          "highlightOpacity": 0.35,
          "groupHistory": { "importBatchSize": 200, "parallelDownloadWorkers": 12, "zkillDocumentedMaxRequestsPerSecond": 8 },
          "style": { "blobMinimumAverageAttackers": 5, "fleetMinimumAverageAttackers": 11 }
        }
        """);

        try
        {
            var settings = ApplicationSettingsLoader.LoadOrDefault(path);

            Assert.Equal(21, settings.RecentWindowDays);
            Assert.Equal(7, settings.BackupRotationCount);
            Assert.Equal(0.35, settings.HighlightOpacity);
            Assert.Equal(200, settings.GroupHistory.ImportBatchSize);
        }
        finally
        {
            System.IO.File.Delete(path);
        }
    }
#endif
}

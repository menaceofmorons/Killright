using Killright.UI.Configuration;
using Xunit;

namespace Killright.UI.Tests.Configuration;

public sealed class DatabaseSettingsTests
{
    [Fact]
    public void Defaults_AreFiveSecondsSixtyFourMegabytesAndThirtySeconds()
    {
        var settings = new DatabaseSettings();

        Assert.Equal(5, settings.BusyTimeoutSeconds);
        Assert.Equal(64, settings.PageCacheMegabytes);
        Assert.Equal(30, settings.IdleCheckpointSeconds);
    }

    [Fact]
    public void LoadOrDefault_RealSettingsFile_ReadsDatabaseSection()
    {
        var settings = ApplicationSettingsLoader.LoadOrDefault(ApplicationSettingsLoader.GetDefaultSettingsPath());

        Assert.Equal(5, settings.Database.BusyTimeoutSeconds);
        Assert.Equal(64, settings.Database.PageCacheMegabytes);
        Assert.Equal(30, settings.Database.IdleCheckpointSeconds);
    }

    [Fact]
    public void LoadOrDefault_SectionAbsent_UsesDefaults()
    {
        var settings = LoadJson("{ \"recentWindowDays\": 14 }");

        Assert.Equal(new DatabaseSettings(), settings.Database);
    }

    [Fact]
    public void LoadOrDefault_ValidValues_AreRead()
    {
        var settings = LoadJson("{ \"database\": { \"busyTimeoutSeconds\": 9, \"pageCacheMegabytes\": 128, \"idleCheckpointSeconds\": 10 } }");

        Assert.Equal(9, settings.Database.BusyTimeoutSeconds);
        Assert.Equal(128, settings.Database.PageCacheMegabytes);
        Assert.Equal(10, settings.Database.IdleCheckpointSeconds);
    }

    [Fact]
    public void LoadOrDefault_NonPositiveValues_FallBackToDefaults()
    {
        var settings = LoadJson("{ \"database\": { \"busyTimeoutSeconds\": 0, \"pageCacheMegabytes\": -1, \"idleCheckpointSeconds\": -5 } }");

        Assert.Equal(new DatabaseSettings(), settings.Database);
    }

    [Fact]
    public void LoadOrDefault_OneNonPositiveValue_KeepsTheOtherValues()
    {
        var settings = LoadJson("{ \"database\": { \"busyTimeoutSeconds\": 0, \"pageCacheMegabytes\": 32, \"idleCheckpointSeconds\": 45 } }");

        Assert.Equal(5, settings.Database.BusyTimeoutSeconds);
        Assert.Equal(32, settings.Database.PageCacheMegabytes);
        Assert.Equal(45, settings.Database.IdleCheckpointSeconds);
    }

    [Fact]
    public void LoadOrDefault_PartialSection_KeepsDefaultsForMissingValues()
    {
        var settings = LoadJson("{ \"database\": { \"pageCacheMegabytes\": 8 } }");

        Assert.Equal(5, settings.Database.BusyTimeoutSeconds);
        Assert.Equal(8, settings.Database.PageCacheMegabytes);
        Assert.Equal(30, settings.Database.IdleCheckpointSeconds);
    }

    [Fact]
    public void LoadOrDefault_FileStillHoldingMemoryLimitAndThreads_LoadsWithTheNewValuesIntact()
    {
        var settings = LoadJson("{ \"database\": { \"memoryLimit\": \"512MB\", \"threads\": 2, \"busyTimeoutSeconds\": 7, \"idleCheckpointSeconds\": 12 } }");

        Assert.Equal(7, settings.Database.BusyTimeoutSeconds);
        Assert.Equal(64, settings.Database.PageCacheMegabytes);
        Assert.Equal(12, settings.Database.IdleCheckpointSeconds);
    }

    private static ApplicationSettings LoadJson(string json)
    {
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"killright-settings-{Guid.NewGuid():N}.json");
        System.IO.File.WriteAllText(path, json);

        try
        {
            return ApplicationSettingsLoader.LoadOrDefault(path);
        }
        finally
        {
            System.IO.File.Delete(path);
        }
    }
}

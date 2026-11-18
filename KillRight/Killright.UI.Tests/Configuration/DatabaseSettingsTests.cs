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
    public void Load_RealSettingsFile_ReadsDatabaseSection()
    {
        var settings = ApplicationSettingsLoader.Load(ApplicationSettingsLoader.GetDefaultSettingsPath()).Settings!;

        Assert.Equal(5, settings.Database.BusyTimeoutSeconds);
        Assert.Equal(64, settings.Database.PageCacheMegabytes);
        Assert.Equal(30, settings.Database.IdleCheckpointSeconds);
    }

    [Fact]
    public void Load_SectionAbsent_UsesDefaults()
    {
        var settings = LoadJson("{ \"recentWindowDays\": 14 }");

        Assert.Equal(new DatabaseSettings(), settings.Database);
    }

    [Fact]
    public void Load_ValidValues_AreRead()
    {
        var settings = LoadJson("{ \"database\": { \"busyTimeoutSeconds\": 9, \"pageCacheMegabytes\": 128, \"idleCheckpointSeconds\": 10 } }");

        Assert.Equal(9, settings.Database.BusyTimeoutSeconds);
        Assert.Equal(128, settings.Database.PageCacheMegabytes);
        Assert.Equal(10, settings.Database.IdleCheckpointSeconds);
    }

    [Fact]
    public void Load_NonPositiveValues_AreSetToTheMinimumAndLogged()
    {
        var log = new List<string>();

        var settings = LoadJson("{ \"database\": { \"busyTimeoutSeconds\": 0, \"pageCacheMegabytes\": -1, \"idleCheckpointSeconds\": -5 } }", log);

        Assert.Equal(1, settings.Database.BusyTimeoutSeconds);
        Assert.Equal(1, settings.Database.PageCacheMegabytes);
        Assert.Equal(1, settings.Database.IdleCheckpointSeconds);
        Assert.Contains("Setting 'database.busyTimeoutSeconds' read as 0; using 1 (below the minimum of 1).", log);
        Assert.Contains("Setting 'database.pageCacheMegabytes' read as -1; using 1 (below the minimum of 1).", log);
        Assert.Contains("Setting 'database.idleCheckpointSeconds' read as -5; using 1 (below the minimum of 1).", log);
    }

    [Fact]
    public void Load_MissingDatabaseValues_TakeDefaultsAndAreLogged()
    {
        var log = new List<string>();

        var settings = LoadJson("{ }", log);

        Assert.Equal(new DatabaseSettings(), settings.Database);
        Assert.Contains("Setting 'database.busyTimeoutSeconds' read as missing; using the default 5.", log);
        Assert.Contains("Setting 'database.pageCacheMegabytes' read as missing; using the default 64.", log);
        Assert.Contains("Setting 'database.idleCheckpointSeconds' read as missing; using the default 30.", log);
    }

    [Fact]
    public void Load_OneNonPositiveValue_KeepsTheOtherValues()
    {
        var settings = LoadJson("{ \"database\": { \"busyTimeoutSeconds\": 0, \"pageCacheMegabytes\": 32, \"idleCheckpointSeconds\": 45 } }");

        Assert.Equal(1, settings.Database.BusyTimeoutSeconds);
        Assert.Equal(32, settings.Database.PageCacheMegabytes);
        Assert.Equal(45, settings.Database.IdleCheckpointSeconds);
    }

    [Fact]
    public void Load_PartialSection_KeepsDefaultsForMissingValues()
    {
        var settings = LoadJson("{ \"database\": { \"pageCacheMegabytes\": 8 } }");

        Assert.Equal(5, settings.Database.BusyTimeoutSeconds);
        Assert.Equal(8, settings.Database.PageCacheMegabytes);
        Assert.Equal(30, settings.Database.IdleCheckpointSeconds);
    }

    [Fact]
    public void Load_FileStillHoldingMemoryLimitAndThreads_LoadsWithTheNewValuesIntact()
    {
        var settings = LoadJson("{ \"database\": { \"memoryLimit\": \"512MB\", \"threads\": 2, \"busyTimeoutSeconds\": 7, \"idleCheckpointSeconds\": 12 } }");

        Assert.Equal(7, settings.Database.BusyTimeoutSeconds);
        Assert.Equal(64, settings.Database.PageCacheMegabytes);
        Assert.Equal(12, settings.Database.IdleCheckpointSeconds);
    }

    private static ApplicationSettings LoadJson(string json, List<string>? log = null)
    {
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"killright-settings-{Guid.NewGuid():N}.json");
        System.IO.File.WriteAllText(path, json);

        try
        {
            return ApplicationSettingsLoader.Load(path, log is null ? null : log.Add).Settings!;
        }
        finally
        {
            System.IO.File.Delete(path);
        }
    }
}

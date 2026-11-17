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

    [Theory]
    [InlineData("busyTimeoutSeconds")]
    [InlineData("pageCacheMegabytes")]
    [InlineData("idleCheckpointSeconds")]
    public void Load_NonPositiveValue_FailsNamingTheSetting(string name)
    {
        var result = LoadResult($"{{ \"database\": {{ \"{name}\": 0 }} }}");

        Assert.False(result.Succeeded);
        Assert.Contains($"database.{name}", result.Failure);
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

    private static ApplicationSettings LoadJson(string json)
    {
        var result = LoadResult(json);

        Assert.True(result.Succeeded, result.Failure);

        return result.Settings!;
    }

    private static SettingsLoadResult LoadResult(string json)
    {
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"killright-settings-{Guid.NewGuid():N}.json");
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
}

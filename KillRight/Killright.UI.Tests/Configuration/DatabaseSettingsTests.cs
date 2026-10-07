using Killright.UI.Configuration;
using Xunit;

namespace Killright.UI.Tests.Configuration;

public sealed class DatabaseSettingsTests
{
    [Fact]
    public void Defaults_AreOneGigabyteFourThreadsAndThirtySeconds()
    {
        var settings = new DatabaseSettings();

        Assert.Equal("1GB", settings.MemoryLimit);
        Assert.Equal(4, settings.Threads);
        Assert.Equal(30, settings.IdleCheckpointSeconds);
    }

    [Fact]
    public void LoadOrDefault_RealSettingsFile_ReadsDatabaseSection()
    {
        var settings = ApplicationSettingsLoader.LoadOrDefault(ApplicationSettingsLoader.GetDefaultSettingsPath());

        Assert.Equal("1GB", settings.Database.MemoryLimit);
        Assert.Equal(4, settings.Database.Threads);
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
        var settings = LoadJson("{ \"database\": { \"memoryLimit\": \"512MB\", \"threads\": 2, \"idleCheckpointSeconds\": 10 } }");

        Assert.Equal("512MB", settings.Database.MemoryLimit);
        Assert.Equal(2, settings.Database.Threads);
        Assert.Equal(10, settings.Database.IdleCheckpointSeconds);
    }

    [Fact]
    public void LoadOrDefault_InvalidValues_FallBackToDefaults()
    {
        var settings = LoadJson("{ \"database\": { \"memoryLimit\": \"lots\", \"threads\": 0, \"idleCheckpointSeconds\": -5 } }");

        Assert.Equal(new DatabaseSettings(), settings.Database);
    }

    [Fact]
    public void LoadOrDefault_BlankMemoryLimit_FallsBackToDefaultAndKeepsTheOtherValues()
    {
        var settings = LoadJson("{ \"database\": { \"memoryLimit\": \"  \", \"threads\": 6, \"idleCheckpointSeconds\": 45 } }");

        Assert.Equal("1GB", settings.Database.MemoryLimit);
        Assert.Equal(6, settings.Database.Threads);
        Assert.Equal(45, settings.Database.IdleCheckpointSeconds);
    }

    [Fact]
    public void LoadOrDefault_PartialSection_KeepsDefaultsForMissingValues()
    {
        var settings = LoadJson("{ \"database\": { \"threads\": 8 } }");

        Assert.Equal("1GB", settings.Database.MemoryLimit);
        Assert.Equal(8, settings.Database.Threads);
        Assert.Equal(30, settings.Database.IdleCheckpointSeconds);
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

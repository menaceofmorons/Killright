using Killright.UI.Configuration;
using Xunit;

namespace Killright.UI.Tests.Configuration;

public sealed class NetworkSettingsTests
{
    [Fact]
    public void LoadOrDefault_RealSettingsFile_ReadsNetworkSection()
    {
        var settings = ApplicationSettingsLoader.LoadOrDefault(ApplicationSettingsLoader.GetDefaultSettingsPath());

        Assert.Equal(15, settings.Network.ZkillRequestsPerSecond);
        Assert.Equal(8, settings.Network.MaxConcurrency);
    }

    [Fact]
    public void LoadOrDefault_SectionAbsent_UsesDefaults()
    {
        var settings = LoadJson("{ \"recentWindowDays\": 14 }");

        Assert.Equal(NetworkSettings.DefaultZkillRequestsPerSecond, settings.Network.ZkillRequestsPerSecond);
        Assert.Equal(NetworkSettings.DefaultMaxConcurrency, settings.Network.MaxConcurrency);
    }

    [Fact]
    public void LoadOrDefault_ValidValues_AreRead()
    {
        var settings = LoadJson("{ \"network\": { \"zkillRequestsPerSecond\": 10, \"maxConcurrency\": 4 } }");

        Assert.Equal(10, settings.Network.ZkillRequestsPerSecond);
        Assert.Equal(4, settings.Network.MaxConcurrency);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(-5, -1)]
    public void LoadOrDefault_InvalidValues_FallBackToDefaults(double requestsPerSecond, int maxConcurrency)
    {
        var settings = LoadJson($"{{ \"network\": {{ \"zkillRequestsPerSecond\": {requestsPerSecond}, \"maxConcurrency\": {maxConcurrency} }} }}");

        Assert.Equal(NetworkSettings.DefaultZkillRequestsPerSecond, settings.Network.ZkillRequestsPerSecond);
        Assert.Equal(NetworkSettings.DefaultMaxConcurrency, settings.Network.MaxConcurrency);
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

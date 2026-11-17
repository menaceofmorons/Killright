using Killright.UI.Configuration;
using Xunit;

namespace Killright.UI.Tests.Configuration;

public sealed class NetworkSettingsTests
{
    [Fact]
    public void Load_RealSettingsFile_ReadsNetworkSection()
    {
        var settings = ApplicationSettingsLoader.Load(ApplicationSettingsLoader.GetDefaultSettingsPath()).Settings!;

        Assert.Equal(8, settings.Network.EsiMaxConcurrency);
        Assert.Equal(550, settings.Network.Zkill.RequestBudget);
        Assert.Equal(60, settings.Network.Zkill.BudgetWindowSeconds);
        Assert.Equal(50, settings.Network.Zkill.MaxConcurrency);
        Assert.Equal(3, settings.Network.Zkill.RequestTimeoutSeconds);
        Assert.Equal(1, settings.Network.Zkill.RetryCount);
        Assert.Equal(60, settings.Network.Zkill.RateLimitPauseSeconds);
    }

    [Fact]
    public void Load_SectionAbsent_UsesDefaults()
    {
        var settings = LoadJson("{ \"recentWindowDays\": 14 }");

        Assert.Equal(NetworkSettings.DefaultEsiMaxConcurrency, settings.Network.EsiMaxConcurrency);
        Assert.Equal(ZkillSettings.DefaultRequestBudget, settings.Network.Zkill.RequestBudget);
        Assert.Equal(ZkillSettings.DefaultBudgetWindowSeconds, settings.Network.Zkill.BudgetWindowSeconds);
        Assert.Equal(ZkillSettings.DefaultMaxConcurrency, settings.Network.Zkill.MaxConcurrency);
        Assert.Equal(ZkillSettings.DefaultRequestTimeoutSeconds, settings.Network.Zkill.RequestTimeoutSeconds);
        Assert.Equal(ZkillSettings.DefaultRetryCount, settings.Network.Zkill.RetryCount);
        Assert.Equal(ZkillSettings.DefaultRateLimitPauseSeconds, settings.Network.Zkill.RateLimitPauseSeconds);
    }

    [Fact]
    public void Load_NetworkSectionWithoutZkill_UsesZkillDefaults()
    {
        var settings = LoadJson("{ \"network\": { \"esiMaxConcurrency\": 4 } }");

        Assert.Equal(4, settings.Network.EsiMaxConcurrency);
        Assert.Equal(ZkillSettings.DefaultRequestBudget, settings.Network.Zkill.RequestBudget);
        Assert.Equal(ZkillSettings.DefaultMaxConcurrency, settings.Network.Zkill.MaxConcurrency);
    }

    [Fact]
    public void Load_ZkillSectionNull_Fails()
    {
        var result = LoadResult("{ \"network\": { \"zkill\": null } }");

        Assert.False(result.Succeeded);
        Assert.Contains("null", result.Failure);
    }

    [Fact]
    public void Load_ValidValues_AreRead()
    {
        var settings = LoadJson(
            "{ \"network\": { \"esiMaxConcurrency\": 4, \"zkill\": { \"requestBudget\": 300, \"budgetWindowSeconds\": 30, \"maxConcurrency\": 20, \"requestTimeoutSeconds\": 5, \"retryCount\": 2, \"rateLimitPauseSeconds\": 90 } } }");

        Assert.Equal(4, settings.Network.EsiMaxConcurrency);
        Assert.Equal(300, settings.Network.Zkill.RequestBudget);
        Assert.Equal(30, settings.Network.Zkill.BudgetWindowSeconds);
        Assert.Equal(20, settings.Network.Zkill.MaxConcurrency);
        Assert.Equal(5, settings.Network.Zkill.RequestTimeoutSeconds);
        Assert.Equal(2, settings.Network.Zkill.RetryCount);
        Assert.Equal(90, settings.Network.Zkill.RateLimitPauseSeconds);
    }

    [Theory]
    [InlineData("esiMaxConcurrency", "network.esiMaxConcurrency")]
    [InlineData("zkill.requestBudget", "network.zkill.requestBudget")]
    [InlineData("zkill.budgetWindowSeconds", "network.zkill.budgetWindowSeconds")]
    [InlineData("zkill.maxConcurrency", "network.zkill.maxConcurrency")]
    [InlineData("zkill.requestTimeoutSeconds", "network.zkill.requestTimeoutSeconds")]
    [InlineData("zkill.retryCount", "network.zkill.retryCount")]
    [InlineData("zkill.rateLimitPauseSeconds", "network.zkill.rateLimitPauseSeconds")]
    public void Load_NonPositiveValue_FailsNamingTheSetting(string path, string expectedName)
    {
        foreach (var value in new[] { 0, -5 })
        {
            var parts = path.Split('.');
            var inner = parts.Length == 1
                ? $"\"{parts[0]}\": {value}"
                : $"\"zkill\": {{ \"{parts[1]}\": {value} }}";

            var result = LoadResult($"{{ \"network\": {{ {inner} }} }}");

            Assert.False(result.Succeeded);
            Assert.Contains(expectedName, result.Failure);
        }
    }

    [Fact]
    public void Load_RetryCountZero_IsRejectedNotMappedToOne()
    {
        var result = LoadResult("{ \"network\": { \"zkill\": { \"retryCount\": 0 } } }");

        Assert.False(result.Succeeded);
        Assert.Null(result.Settings);
        Assert.Contains("network.zkill.retryCount", result.Failure);
    }

    [Theory]
    [InlineData(601)]
    [InlineData(5000)]
    public void Load_RequestBudgetAboveCap_Fails(int configured)
    {
        var result = LoadResult($"{{ \"network\": {{ \"zkill\": {{ \"requestBudget\": {configured} }} }} }}");

        Assert.False(result.Succeeded);
        Assert.Contains("network.zkill.requestBudget", result.Failure);
    }

    [Fact]
    public void Load_RequestBudgetAtCap_IsRead()
    {
        Assert.Equal(600, LoadJson("{ \"network\": { \"zkill\": { \"requestBudget\": 600 } } }").Network.Zkill.RequestBudget);
    }

    [Fact]
    public void Load_RemovedSettingNames_AreIgnored()
    {
        var settings = LoadJson("{ \"network\": { \"zkillRequestsPerSecond\": 99, \"maxConcurrency\": 3 } }");

        Assert.Equal(NetworkSettings.DefaultEsiMaxConcurrency, settings.Network.EsiMaxConcurrency);
        Assert.Equal(ZkillSettings.DefaultMaxConcurrency, settings.Network.Zkill.MaxConcurrency);
    }

    [Fact]
    public void ScanAndIdentityResolver_ReceiveTheirOwnConcurrencySetting()
    {
        var app = ReadSource("App.xaml.cs");
        var mainWindow = ReadSource("MainWindow.xaml.cs");

        Assert.Contains("Settings.Network.EsiMaxConcurrency", app);
        Assert.DoesNotContain("Settings.Network.MaxConcurrency", app);
        Assert.Contains("App.Settings.Network.Zkill.MaxConcurrency", mainWindow);
        Assert.DoesNotContain("Settings.Network.MaxConcurrency", mainWindow);
    }

    [Fact]
    public void App_ZkillHttpClientHasInfiniteTimeoutAndBudgetFromSettings()
    {
        var app = ReadSource("App.xaml.cs");

        Assert.Contains("Timeout = Timeout.InfiniteTimeSpan", app);
        Assert.Contains("new RollingWindowRequestBudget(", app);
        Assert.DoesNotContain("RequestStartLimiter(", app);
    }

    [Fact]
    public void MainWindow_ScanRecordsZkillCountersAndBudgetWait()
    {
        var source = ReadSource("MainWindow.xaml.cs");

        Assert.Contains("\"zkill_timeouts\"", source);
        Assert.Contains("\"zkill_retries\"", source);
        Assert.Contains("\"zkill_429\"", source);
        Assert.Contains("\"zkill_paused_rejects\"", source);
        Assert.Contains("\"budget_wait_ms\"", source);
        Assert.DoesNotContain("limiter_wait_ms", source);
    }

    [Fact]
    public void DefaultHttpClientHandler_DoesNotCapConnectionsBelowZkillConcurrency()
    {
        using var handler = new System.Net.Http.HttpClientHandler();

        Assert.True(handler.MaxConnectionsPerServer >= ZkillSettings.DefaultMaxConcurrency);
    }

    private static string ReadSource(string fileName)
    {
        var directory = new System.IO.DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !System.IO.File.Exists(System.IO.Path.Combine(directory.FullName, "Killright.UI", fileName)))
            directory = directory.Parent;

        Assert.NotNull(directory);

        return System.IO.File.ReadAllText(System.IO.Path.Combine(directory!.FullName, "Killright.UI", fileName));
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

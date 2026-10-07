using Killright.UI.Configuration;
using Xunit;

namespace Killright.UI.Tests.Configuration;

public sealed class NetworkSettingsTests
{
    [Fact]
    public void LoadOrDefault_RealSettingsFile_ReadsNetworkSection()
    {
        var settings = ApplicationSettingsLoader.LoadOrDefault(ApplicationSettingsLoader.GetDefaultSettingsPath());

        Assert.Equal(8, settings.Network.EsiMaxConcurrency);
        Assert.Equal(550, settings.Network.Zkill.RequestBudget);
        Assert.Equal(60, settings.Network.Zkill.BudgetWindowSeconds);
        Assert.Equal(50, settings.Network.Zkill.MaxConcurrency);
        Assert.Equal(3, settings.Network.Zkill.RequestTimeoutSeconds);
        Assert.Equal(1, settings.Network.Zkill.RetryCount);
        Assert.Equal(60, settings.Network.Zkill.RateLimitPauseSeconds);
    }

    [Fact]
    public void LoadOrDefault_SectionAbsent_UsesDefaults()
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
    public void LoadOrDefault_NetworkSectionWithoutZkill_UsesZkillDefaults()
    {
        var settings = LoadJson("{ \"network\": { \"esiMaxConcurrency\": 4 } }");

        Assert.Equal(4, settings.Network.EsiMaxConcurrency);
        Assert.Equal(ZkillSettings.DefaultRequestBudget, settings.Network.Zkill.RequestBudget);
        Assert.Equal(ZkillSettings.DefaultMaxConcurrency, settings.Network.Zkill.MaxConcurrency);
    }

    [Fact]
    public void LoadOrDefault_ZkillSectionNull_UsesZkillDefaults()
    {
        var settings = LoadJson("{ \"network\": { \"zkill\": null } }");

        Assert.Equal(ZkillSettings.DefaultRequestBudget, settings.Network.Zkill.RequestBudget);
    }

    [Fact]
    public void LoadOrDefault_ValidValues_AreRead()
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
    [InlineData(0)]
    [InlineData(-5)]
    public void LoadOrDefault_NonPositiveValues_FallBackToDefaults(int value)
    {
        var settings = LoadJson(
            $"{{ \"network\": {{ \"esiMaxConcurrency\": {value}, \"zkill\": {{ \"requestBudget\": {value}, \"budgetWindowSeconds\": {value}, \"maxConcurrency\": {value}, \"requestTimeoutSeconds\": {value}, \"retryCount\": {value}, \"rateLimitPauseSeconds\": {value} }} }} }}");

        Assert.Equal(NetworkSettings.DefaultEsiMaxConcurrency, settings.Network.EsiMaxConcurrency);
        Assert.Equal(ZkillSettings.DefaultRequestBudget, settings.Network.Zkill.RequestBudget);
        Assert.Equal(ZkillSettings.DefaultBudgetWindowSeconds, settings.Network.Zkill.BudgetWindowSeconds);
        Assert.Equal(ZkillSettings.DefaultMaxConcurrency, settings.Network.Zkill.MaxConcurrency);
        Assert.Equal(ZkillSettings.DefaultRequestTimeoutSeconds, settings.Network.Zkill.RequestTimeoutSeconds);
        Assert.Equal(ZkillSettings.DefaultRetryCount, settings.Network.Zkill.RetryCount);
        Assert.Equal(ZkillSettings.DefaultRateLimitPauseSeconds, settings.Network.Zkill.RateLimitPauseSeconds);
    }

    [Theory]
    [InlineData(601, 600)]
    [InlineData(5000, 600)]
    [InlineData(600, 600)]
    public void LoadOrDefault_RequestBudgetAboveCap_IsCappedAt600(int configured, int expected)
    {
        var settings = LoadJson($"{{ \"network\": {{ \"zkill\": {{ \"requestBudget\": {configured} }} }} }}");

        Assert.Equal(expected, settings.Network.Zkill.RequestBudget);
    }

    [Fact]
    public void LoadOrDefault_RemovedSettingNames_AreIgnored()
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

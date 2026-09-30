using Xunit;

namespace Killright.UI.Tests.InfoSheet;

public sealed class InfoSheetLastActivitySourceTests
{
    [Fact]
    public void MainWindow_LoadsLastActivityThroughTheResolverAndNotTheLocalCacheDirectly()
    {
        var source = Read("MainWindow.xaml.cs");

        Assert.Contains("App.LastActivityResolver.ResolveAsync(characterId)", source);
        Assert.DoesNotContain("App.RecentKillmailCache.GetMostRecentKillmailAsync", source);
    }

    [Fact]
    public void MainWindow_ScanPathNeverCallsTheLastKillmailLookup()
    {
        Assert.DoesNotContain("GetLastKillmailAsync", Read("MainWindow.xaml.cs"));
    }

    [Fact]
    public void MainWindow_PopupLoadRowCarriesTheLookupTagAndRequestCounter()
    {
        var source = Read("MainWindow.xaml.cs");

        Assert.Contains("scope.Tag = lookupTag.Value;", source);
        Assert.Contains("ScanTimings.CounterPrefix + \"zkill_requests\"", source);
        Assert.Contains("PilotLastActivitySource.Live ? \"live\" : \"cache\"", source);
        Assert.Contains("lookupTag.Value = \"failed\"", source);
    }

    [Fact]
    public void Diagnostics_ClearActivityCacheClearsTheLastKillmailCache()
    {
        var body = Between(Read("Diagnostics/DiagnosticsView.xaml.cs"), "private void ClearActivityCache()", "private void ClearKillmailCache()");

        Assert.Contains("DELETE FROM main.pilot_last_killmail_cache;", body);
        Assert.Contains("DELETE FROM main.zkill_activity_cache;", body);
        Assert.Contains("UPDATE main.zkill_statistics_cache SET months_processed = NULL;", body);
    }

    [Fact]
    public void Diagnostics_OnlyClearActivityCacheUnsetsTheStatisticsMonthsProcessedFlag()
    {
        var source = Read("Diagnostics/DiagnosticsView.xaml.cs");
        var killmailBody = Between(source, "private void ClearKillmailCache()", "private void ClearStatisticsCache()");
        var statisticsBody = Between(source, "private void ClearStatisticsCache()", "private void CopySummary_Click");

        Assert.DoesNotContain("months_processed", killmailBody);
        Assert.DoesNotContain("months_processed", statisticsBody);
    }

    [Fact]
    public void Diagnostics_ClearRecentKillmailCacheLeavesTheLastKillmailCacheAlone()
    {
        var body = Between(Read("Diagnostics/DiagnosticsView.xaml.cs"), "private void ClearKillmailCache()", "private void ClearStatisticsCache()");

        Assert.DoesNotContain("pilot_last_killmail_cache", body);
    }

    private static string Between(string source, string start, string end)
    {
        var from = source.IndexOf(start, StringComparison.Ordinal);
        var to = source.IndexOf(end, StringComparison.Ordinal);

        Assert.True(from >= 0 && to > from, $"markers not found: {start} / {end}");

        return source[from..to];
    }

    private static string Read(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "KillRight.sln")))
            directory = directory.Parent;

        Assert.NotNull(directory);

        return File.ReadAllText(Path.Combine(directory!.FullName, "Killright.UI", relativePath)).Replace("\r\n", "\n");
    }
}

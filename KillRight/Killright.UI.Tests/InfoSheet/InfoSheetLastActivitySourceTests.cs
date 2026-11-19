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

    private static string Read(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "KillRight.sln")))
            directory = directory.Parent;

        Assert.NotNull(directory);

        return File.ReadAllText(Path.Combine(directory!.FullName, "Killright.UI", relativePath)).Replace("\r\n", "\n");
    }
}

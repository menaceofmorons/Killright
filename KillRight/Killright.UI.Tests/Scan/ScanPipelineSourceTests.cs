using Xunit;

namespace Killright.UI.Tests.Scan;

public sealed class ScanPipelineSourceTests
{
    private const string Checkpoint = "cancellationToken.ThrowIfCancellationRequested();";

    [Theory]
    [InlineData("CommitWrites(session, writes, timings, \"scan\");")]
    [InlineData("CommitWrites(null, postEngineWrites, timings, \"post_engine\");")]
    [InlineData("engineResults = await App.RecentStyleClient.AnalyzePilotsAsync(")]
    [InlineData("await AttachGroupRelationshipsAsync(rows, timings);")]
    [InlineData("await ApplyRowsAsync(rows, context, timings);")]
    public void CallSite_IsImmediatelyPrecededByCancellationCheckpoint(string callSite)
    {
        var lines = ReadMainWindow().Split('\n').Select(line => line.Trim()).ToList();
        var index = lines.FindIndex(line => line.StartsWith(callSite, StringComparison.Ordinal));

        Assert.True(index > 0, $"call site not found: {callSite}");

        var previous = index - 1;

        while (previous >= 0 && (lines[previous].Length == 0 || lines[previous] == "{" || lines[previous].StartsWith("using (", StringComparison.Ordinal)))
            previous--;

        Assert.Equal(Checkpoint, lines[previous]);
    }

    [Fact]
    public void EveryStageBoundary_HasACheckpoint()
    {
        var source = ReadMainWindow();
        var core = source[source.IndexOf("private async Task ResolvePilotsCoreAsync(", StringComparison.Ordinal)..source.IndexOf("private async Task ApplyRowsAsync(", StringComparison.Ordinal)];

        Assert.True(core.Split(Checkpoint).Length - 1 >= 8);
    }

    [Fact]
    public void ScanSessionOpenAndClose_AreTimed()
    {
        var source = ReadMainWindow();
        var core = source[source.IndexOf("private async Task ResolvePilotsCoreAsync(", StringComparison.Ordinal)..source.IndexOf("private async Task ApplyRowsAsync(", StringComparison.Ordinal)];

        Assert.Matches(@"timings\.Measure\(ScanTimings\.ScanLevel, ""db_session_open""\)\)\s*\{\s*session = TryOpenScanSession\(\);\s*\}", core);
        Assert.Matches(@"timings\.Measure\(ScanTimings\.ScanLevel, ""db_session_close""\)\)\s*\{\s*session\?\.Dispose\(\);\s*\}", core);
    }

    [Fact]
    public void StallMonitorStart_IsTimed()
    {
        var source = ReadMainWindow();

        Assert.Matches(@"timings\.Measure\(ScanTimings\.ScanLevel, ""stall_monitor_start""\)\)\s*\{\s*stallMonitor = await Dispatcher\.InvokeAsync\(\(\) => UiStallMonitor\.Start\(Dispatcher\)\);\s*\}", source);
    }

    [Fact]
    public void CommitWrites_RecordsOpenCommitAndClosePhases()
    {
        var source = ReadMainWindow();
        var start = source.IndexOf("private static void CommitWrites(", StringComparison.Ordinal);
        var commitWrites = source[start..source.IndexOf("ReadCachedStatisticsAsync(", start, StringComparison.Ordinal)];

        Assert.Contains("\"write_open\", null, tag", commitWrites);
        Assert.Contains("\"write_commit\", null, tag", commitWrites);
        Assert.Contains("\"write_close\", null, tag", commitWrites);
        Assert.True(commitWrites.IndexOf("\"write_open\"", StringComparison.Ordinal) < commitWrites.IndexOf("\"write_commit\"", StringComparison.Ordinal));
        Assert.True(commitWrites.IndexOf("\"write_commit\"", StringComparison.Ordinal) < commitWrites.IndexOf("\"write_close\"", StringComparison.Ordinal));
    }

    [Fact]
    public void NetworkCalls_ReceiveTheScanCancellationToken()
    {
        var source = ReadMainWindow();

        Assert.Contains("App.zKillClient.GetStatisticsAsync(characterId, cancellationToken)", source);
        Assert.Contains("App.zKillClient.GetRecentKillmailsAsync(characterId, pastSeconds, cancellationToken)", source);
        Assert.Contains("App.IdentityResolver.ResolveAsync(pilotNames, timings, cancellationToken, session, writes)", source);
    }

    [Fact]
    public void GridUpdate_OnlyInApplyRowsAndOnlyForTheCurrentScan()
    {
        var source = ReadMainWindow();
        var applyStart = source.IndexOf("private async Task ApplyRowsAsync(", StringComparison.Ordinal);
        var core = source[..applyStart];
        var apply = source[applyStart..];

        Assert.DoesNotContain("_viewModel.Pilots.Clear()", core);
        Assert.DoesNotContain("_viewModel.Pilots.Add(", core);
        Assert.Contains("Dispatcher.InvokeAsync(() =>", apply);
        Assert.Contains("if (!context.IsCurrent())", apply);
        Assert.Contains("_viewModel.Pilots.Clear()", apply);
    }

    [Fact]
    public void ClipboardChanged_HandsTheListToTheCoordinatorAndIsNotAsync()
    {
        var source = ReadMainWindow();

        Assert.DoesNotContain("async void ClipboardChanged", source);
        Assert.Contains("_scanCoordinator.Submit(pilotNames, timings)", source);
        Assert.Contains("App.PurgeScheduler.ScanStarted", source);
        Assert.Contains("App.PurgeScheduler.ScanFinished()", source);
    }

    [Fact]
    public void GridFilters_AreClearedByNewScanCtrlRAndHidingAFilteredColumn()
    {
        var source = ReadMainWindow();
        var applyStart = source.IndexOf("private async Task ApplyRowsAsync(", StringComparison.Ordinal);
        var apply = source[applyStart..];
        var currentCheck = apply.IndexOf("if (!context.IsCurrent())", StringComparison.Ordinal);
        var clearCall = apply.IndexOf("ClearFilters();", StringComparison.Ordinal);
        var rowsClear = apply.IndexOf("_viewModel.Pilots.Clear()", StringComparison.Ordinal);

        Assert.True(currentCheck >= 0 && clearCall > currentCheck && clearCall < rowsClear);
        Assert.Contains("e.Key == Key.R", source);
        Assert.Contains("if (_filters.CarriesFilter(columnId))", source);
        Assert.Contains("_columnsById[columnId].Visibility != Visibility.Visible", source);
        Assert.Contains("if (_filters.IsPilotFilterActive)", source);
    }

    private static string ReadMainWindow()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "KillRight.sln")))
            directory = directory.Parent;

        Assert.NotNull(directory);

        return File.ReadAllText(Path.Combine(directory!.FullName, "Killright.UI", "MainWindow.xaml.cs")).Replace("\r\n", "\n");
    }
}

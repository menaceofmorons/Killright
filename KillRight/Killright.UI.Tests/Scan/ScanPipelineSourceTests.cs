using Xunit;

namespace Killright.UI.Tests.Scan;

public sealed class ScanPipelineSourceTests
{
    private const string Checkpoint = "cancellationToken.ThrowIfCancellationRequested();";

    [Theory]
    [InlineData("CommitWrites(session, writes);")]
    [InlineData("CommitWrites(null, postEngineWrites);")]
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

    private static string ReadMainWindow()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "KillRight.sln")))
            directory = directory.Parent;

        Assert.NotNull(directory);

        return File.ReadAllText(Path.Combine(directory!.FullName, "Killright.UI", "MainWindow.xaml.cs")).Replace("\r\n", "\n");
    }
}

using Xunit;

namespace Killright.UI.Tests.InfoSheet;

public sealed class InfoSheetDismissalSourceTests
{
    [Fact]
    public void MainWindow_PilotCellRightClickMarksHandledAndOpensTheSheetAfterTheMouseUp()
    {
        var body = Between(Read("MainWindow.xaml.cs"), "private void PilotGrid_PreviewMouseRightButtonUp(", "private ");
        var pilotPath = body[body.IndexOf("if (cellColumn == ColumnPilot)", StringComparison.Ordinal)..];

        Assert.Contains("e.Handled = true;", pilotPath);
        Assert.Contains("Dispatcher.BeginInvoke(DispatcherPriority.Input", pilotPath);
        Assert.Contains("OpenInfoSheet(row, screenPosition)", pilotPath);
        Assert.DoesNotContain("OpenInfoSheet(row, PointToScreen(e.GetPosition(this)))", body);
    }

    [Fact]
    public void MainWindow_CtrlRightClickStillAppliesThePilotFilter()
    {
        var body = Between(Read("MainWindow.xaml.cs"), "private void PilotGrid_PreviewMouseRightButtonUp(", "private ");

        Assert.Contains("ApplyPilotFilter(row);", body);
    }

    [Fact]
    public void InfoSheetWindow_DismissalIsArmedOnlyAfterTheSheetHasRenderedAndTheDispatcherIsIdle()
    {
        var source = Read("InfoSheet/InfoSheetWindow.xaml.cs");

        Assert.Contains("_dismissArmed", source);
        Assert.Contains("ContentRendered += InfoSheetWindow_ContentRendered;", source);
        Assert.Contains("DispatcherPriority.ApplicationIdle", source);
        Assert.Contains("_dismissArmed = true;", source);
        Assert.Contains("if (!_dismissArmed || _closeScheduled)", source);
        Assert.Contains("Dispatcher.BeginInvoke(DispatcherPriority.Background, Close)", source);
    }

    [Fact]
    public void InfoSheetWindowXaml_StillBindsTheDeactivatedHandler()
    {
        Assert.Contains("Deactivated=\"InfoSheetWindow_Deactivated\"", Read("InfoSheet/InfoSheetWindow.xaml"));
    }

    [Fact]
    public void MainWindow_ClosedHandlerClearsTheReferenceOnlyForItsOwnSheet()
    {
        Assert.Contains("ReferenceEquals(_infoSheet, infoSheet)", Read("MainWindow.xaml.cs"));
    }

    private static string Between(string source, string start, string end)
    {
        var from = source.IndexOf(start, StringComparison.Ordinal);
        var to = source.IndexOf(end, from + start.Length, StringComparison.Ordinal);

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

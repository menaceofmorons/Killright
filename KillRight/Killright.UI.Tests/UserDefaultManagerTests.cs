using Killright.Shared;
using Killright.UI.UiState;
using Xunit;

namespace Killright.UI.Tests;

public class UserDefaultManagerTests
{
    [Fact]
    public void CaptureFrom_CopiesSnapshotScopeOnly()
    {
        var state = new UiStateModel
        {
            WindowLeft = 50,
            WindowTop = 60,
            WindowWidth = 800,
            WindowHeight = 400,
            AlwaysOnTop = false,
            Theme = AppTheme.Dark,
            GridFontTier = GridFontTier.Large,
            Columns = new[] { new ColumnState { Id = ColumnIds.Pilot, DisplayIndex = 0, Width = 170, Visible = true } },
            PilotHighlightColorHex = "#FF112233",
            RelatedHighlightColorHex = "#FF445566",
            NewPilotColorHex = "#FF778899",
            DeveloperTabRevealed = true,
            IgnoreListEntries = new[] { new IgnoreListEntry { Id = 1, Type = IgnoreEntryType.Pilot, Name = "T'ral Vsengne" } }
        };

        var snapshot = UserDefaultManager.CaptureFrom(state);

        Assert.Equal(state.WindowLeft, snapshot.WindowLeft);
        Assert.Equal(state.WindowTop, snapshot.WindowTop);
        Assert.Equal(state.WindowWidth, snapshot.WindowWidth);
        Assert.Equal(state.WindowHeight, snapshot.WindowHeight);
        Assert.Equal(state.AlwaysOnTop, snapshot.AlwaysOnTop);
        Assert.Equal(state.Theme, snapshot.Theme);
        Assert.Equal(state.GridFontTier, snapshot.GridFontTier);
        Assert.Single(snapshot.Columns);
        Assert.Equal(state.PilotHighlightColorHex, snapshot.PilotHighlightColorHex);
        Assert.Equal(state.RelatedHighlightColorHex, snapshot.RelatedHighlightColorHex);
        Assert.Equal("#FF778899", snapshot.NewPilotColorHex);
    }

    [Fact]
    public void ApplyTo_RestoresNewPilotColorIncludingThemeDefaultNull()
    {
        var state = new UiStateModel { NewPilotColorHex = "#FF112233" };

        var restoredColour = UserDefaultManager.ApplyTo(
            state, new UserDefaultSnapshot { NewPilotColorHex = "#FFAABBCC" }, 0, 0, 1920, 1080);
        var restoredDefault = UserDefaultManager.ApplyTo(
            state, new UserDefaultSnapshot { NewPilotColorHex = null }, 0, 0, 1920, 1080);

        Assert.Equal("#FFAABBCC", restoredColour.NewPilotColorHex);
        Assert.Null(restoredDefault.NewPilotColorHex);
    }

    [Fact]
    public void SystemDefault_NewPilotColorIsThemeDefaultNull()
    {
        Assert.Null(UiStateDefaults.NewPilotColorHex);
        Assert.Null(new UiStateModel().NewPilotColorHex);
        Assert.Null(new UserDefaultSnapshot().NewPilotColorHex);
    }

    [Fact]
    public void ApplyTo_RestoresSnapshotAndPreservesIgnoreListAndDeveloperTabRevealed()
    {
        var state = new UiStateModel
        {
            WindowLeft = 999,
            Theme = AppTheme.Light,
            DeveloperTabRevealed = true,
            IgnoreListEntries = new[] { new IgnoreListEntry { Id = 1, Type = IgnoreEntryType.Pilot, Name = "syMptom NZ" } }
        };

        var snapshot = new UserDefaultSnapshot
        {
            WindowLeft = 10,
            WindowTop = 20,
            WindowWidth = 720,
            WindowHeight = 360,
            AlwaysOnTop = true,
            Theme = AppTheme.Dark,
            GridFontTier = GridFontTier.Small,
            Columns = UiStateDefaults.DefaultColumns,
            PilotHighlightColorHex = "#FFAAAAAA",
            RelatedHighlightColorHex = "#FFBBBBBB"
        };

        var result = UserDefaultManager.ApplyTo(
            state,
            snapshot,
            virtualScreenLeft: 0,
            virtualScreenTop: 0,
            virtualScreenWidth: 1920,
            virtualScreenHeight: 1080);

        Assert.Equal(10, result.WindowLeft);
        Assert.Equal(20, result.WindowTop);
        Assert.Equal(AppTheme.Dark, result.Theme);
        Assert.True(result.DeveloperTabRevealed);
        Assert.Single(result.IgnoreListEntries);
    }

    [Fact]
    public void ApplyTo_SnapshotPositionOffDisconnectedMonitor_ClampsIntoVirtualScreen()
    {
        var state = new UiStateModel();
        var snapshot = new UserDefaultSnapshot { WindowLeft = 5000, WindowTop = 5000, WindowWidth = 720, WindowHeight = 360 };

        var result = UserDefaultManager.ApplyTo(
            state,
            snapshot,
            virtualScreenLeft: 0,
            virtualScreenTop: 0,
            virtualScreenWidth: 1920,
            virtualScreenHeight: 1080);

        Assert.Equal(1200, result.WindowLeft);
        Assert.Equal(720, result.WindowTop);
    }
}

using Killright.UI.UiState;
using Xunit;

namespace Killright.UI.Tests;

public class UiStateDefaultsTests
{
    [Fact]
    public void ReconcileColumns_UnknownColumnAbsent_AppendedVisibleAtEnd()
    {
        var saved = UiStateDefaults.DefaultColumns
            .Where(column => column.Id != ColumnIds.Notes)
            .ToList();

        var reconciled = UiStateDefaults.ReconcileColumns(saved);

        Assert.Equal(UiStateDefaults.DefaultColumns.Count, reconciled.Count);
        var notes = reconciled.Single(column => column.Id == ColumnIds.Notes);
        Assert.False(notes.Visible);
        Assert.Equal(reconciled.Count - 1, notes.DisplayIndex);
    }

    [Fact]
    public void ReconcileColumns_UnknownSavedColumnId_Dropped()
    {
        var saved = UiStateDefaults.DefaultColumns
            .Append(new ColumnState { Id = "RemovedInFutureVersion", DisplayIndex = 99, Width = 50, Visible = true })
            .ToList();

        var reconciled = UiStateDefaults.ReconcileColumns(saved);

        Assert.Equal(UiStateDefaults.DefaultColumns.Count, reconciled.Count);
        Assert.DoesNotContain(reconciled, column => column.Id == "RemovedInFutureVersion");
    }

    [Fact]
    public void ReconcileColumns_PilotHiddenInSavedState_ForcedVisible()
    {
        var saved = UiStateDefaults.DefaultColumns
            .Select(column => column.Id == ColumnIds.Pilot ? column with { Visible = false } : column)
            .ToList();

        var reconciled = UiStateDefaults.ReconcileColumns(saved);

        Assert.True(reconciled.Single(column => column.Id == ColumnIds.Pilot).Visible);
    }

    [Fact]
    public void ReconcileColumns_DisplayIndicesAreContiguousFromZero()
    {
        var saved = UiStateDefaults.DefaultColumns
            .Select((column, index) => column with { DisplayIndex = index * 10 })
            .ToList();

        var reconciled = UiStateDefaults.ReconcileColumns(saved);

        Assert.Equal(Enumerable.Range(0, reconciled.Count), reconciled.OrderBy(column => column.DisplayIndex).Select(column => column.DisplayIndex));
    }

    [Fact]
    public void ReconcileColumns_RetiredStyleColumns_MergedIntoStyleAtEarlierPosition()
    {
        var saved = new[]
        {
            new ColumnState { Id = ColumnIds.Pilot, DisplayIndex = 0, Width = 170, Visible = true },
            new ColumnState { Id = "GeneralStyle", DisplayIndex = 1, Width = 120, Visible = false },
            new ColumnState { Id = "RecentStyle", DisplayIndex = 2, Width = 120, Visible = true },
            new ColumnState { Id = ColumnIds.Threat, DisplayIndex = 3, Width = 90, Visible = true }
        };

        var reconciled = UiStateDefaults.ReconcileColumns(saved);

        Assert.DoesNotContain(reconciled, column => column.Id == "GeneralStyle" || column.Id == "RecentStyle");
        var style = reconciled.Single(column => column.Id == ColumnIds.Style);
        var threat = reconciled.Single(column => column.Id == ColumnIds.Threat);
        var pilot = reconciled.Single(column => column.Id == ColumnIds.Pilot);
        Assert.True(style.Visible);
        Assert.True(style.DisplayIndex > pilot.DisplayIndex);
        Assert.True(style.DisplayIndex < threat.DisplayIndex);
    }

    [Fact]
    public void ReconcileColumns_RetiredWeekColumnsBothHidden_MergedIntoHiddenWeek()
    {
        var saved = new[]
        {
            new ColumnState { Id = ColumnIds.Pilot, DisplayIndex = 0, Width = 170, Visible = true },
            new ColumnState { Id = "KillsWeek", DisplayIndex = 1, Width = 95, Visible = false },
            new ColumnState { Id = "SoloWeek", DisplayIndex = 2, Width = 95, Visible = false }
        };

        var reconciled = UiStateDefaults.ReconcileColumns(saved);

        var week = reconciled.Single(column => column.Id == ColumnIds.Week);
        Assert.False(week.Visible);
    }

    [Fact]
    public void ReconcileColumns_OnlyOneRetiredWeekColumnPresent_StillMergesIntoWeek()
    {
        var saved = new[]
        {
            new ColumnState { Id = ColumnIds.Pilot, DisplayIndex = 0, Width = 170, Visible = true },
            new ColumnState { Id = "KillsWeek", DisplayIndex = 1, Width = 95, Visible = true }
        };

        var reconciled = UiStateDefaults.ReconcileColumns(saved);

        Assert.Single(reconciled, column => column.Id == ColumnIds.Week);
        Assert.True(reconciled.Single(column => column.Id == ColumnIds.Week).Visible);
    }

    [Fact]
    public void IsEffectivelyVisible_DeveloperGatedColumnWithDeveloperTabHidden_ReturnsFalseEvenWhenVisibleFlagTrue()
    {
        var notes = new ColumnState { Id = ColumnIds.Notes, DisplayIndex = 0, Width = 260, Visible = true };

        Assert.False(UiStateDefaults.IsEffectivelyVisible(notes, developerTabRevealed: false));
        Assert.True(UiStateDefaults.IsEffectivelyVisible(notes, developerTabRevealed: true));
    }

    [Fact]
    public void IsEffectivelyVisible_NonGatedColumn_FollowsVisibleFlagRegardlessOfDeveloperTab()
    {
        var style = new ColumnState { Id = ColumnIds.Style, DisplayIndex = 0, Width = 60, Visible = false };

        Assert.False(UiStateDefaults.IsEffectivelyVisible(style, developerTabRevealed: false));
        Assert.False(UiStateDefaults.IsEffectivelyVisible(style, developerTabRevealed: true));
    }
}

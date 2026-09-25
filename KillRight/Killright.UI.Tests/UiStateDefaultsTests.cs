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
        Assert.True(notes.Visible);
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
}

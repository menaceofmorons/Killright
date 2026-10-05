using System.Windows.Media;
using Killright.UI.Configuration;
using Killright.UI.ViewModels;
using Xunit;

namespace Killright.UI.Tests.ViewModels;

public sealed class PilotReportRowBackgroundTests
{
    [Fact]
    public void RowBackgroundKind_DefaultsToNone()
    {
        Assert.Equal(RowBackgroundKind.None, new PilotReportRow().RowBackgroundKind);
    }

    [Fact]
    public void RowBackgroundKind_NewPilotWithoutHighlight_IsNewPilot()
    {
        var row = new PilotReportRow { IsNewPilot = true };

        Assert.Equal(RowBackgroundKind.NewPilot, row.RowBackgroundKind);
    }

    [Fact]
    public void RowBackgroundKind_HighlightOverridesNewPilotAndTintReturnsWhenHighlightClears()
    {
        var row = new PilotReportRow { IsNewPilot = true };

        row.HighlightBrush = new SolidColorBrush(Colors.LightBlue);
        Assert.Equal(RowBackgroundKind.Highlight, row.RowBackgroundKind);
        Assert.True(row.IsNewPilot);

        row.HighlightBrush = null;
        Assert.Equal(RowBackgroundKind.NewPilot, row.RowBackgroundKind);
    }

    [Fact]
    public void RelationshipHighlightCalculator_HoverThenClear_NewPilotTintShowsAgain()
    {
        var hovered = new PilotReportRow { CharacterId = 1, Pilot = "Lukas Naarii" };
        var other = new PilotReportRow { CharacterId = 2, Pilot = "T'ral Vsengne", IsNewPilot = true, CorporationId = 2000001 };
        hovered.CorporationId = 2000001;
        var rows = new[] { hovered, other };

        RelationshipHighlightCalculator.Apply(
            rows, hovered, _ => false, RelationshipConfidenceBandSetting.Defaults, Colors.LightBlue, Colors.LightGreen, 0.2);

        Assert.Equal(RowBackgroundKind.Highlight, hovered.RowBackgroundKind);
        Assert.Equal(RowBackgroundKind.Highlight, other.RowBackgroundKind);

        RelationshipHighlightCalculator.Clear(rows);

        Assert.Equal(RowBackgroundKind.None, hovered.RowBackgroundKind);
        Assert.Equal(RowBackgroundKind.NewPilot, other.RowBackgroundKind);
    }

    [Fact]
    public void PropertyChanged_RaisesRowBackgroundKindForNewPilotAndHighlightChanges()
    {
        var row = new PilotReportRow();
        var raised = new List<string?>();
        row.PropertyChanged += (_, args) => raised.Add(args.PropertyName);

        row.IsNewPilot = true;
        row.HighlightBrush = new SolidColorBrush(Colors.LightBlue);

        Assert.Equal(2, raised.Count(name => name == nameof(PilotReportRow.RowBackgroundKind)));
    }
}

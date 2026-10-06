using Killright.Core.Models;
using Killright.Core.Style;
using Killright.Shared;
using Killright.UI.Resources;
using Killright.UI.UiState;
using Killright.UI.ViewModels;
using Xunit;

namespace Killright.UI.Tests.ViewModels;

public sealed class FactionWarfareColumnTests
{
    [Theory]
    [InlineData(500001L, "Ca")]
    [InlineData(500004L, "Ga")]
    [InlineData(500002L, "Mn")]
    [InlineData(500003L, "Am")]
    [InlineData(500011L, "An")]
    [InlineData(500010L, "Gu")]
    public void FromPilot_EnlistedPilot_ShowsShortCodeForFactionId(long factionId, string expected)
    {
        var row = CreateRow(factionId);

        Assert.Equal(factionId, row.FactionId);
        Assert.Equal(expected, row.FactionWarfare);
    }

    [Fact]
    public void FromPilot_NotEnlisted_ShowsDash()
    {
        var row = CreateRow(null);

        Assert.Null(row.FactionId);
        Assert.Equal("-", row.FactionWarfare);
    }

    [Fact]
    public void FormatFactionWarfare_CountBelowTwo_OmitsCount()
    {
        Assert.Equal("Am", PilotReportRowFactory.FormatFactionWarfare(500003, 1));
        Assert.Equal("Am (2)", PilotReportRowFactory.FormatFactionWarfare(500003, 2));
    }

    [Fact]
    public void DefaultColumns_FactionWarfare_VisibleAndPlacedImmediatelyAfterAlliance()
    {
        var alliance = UiStateDefaults.DefaultColumns.Single(column => column.Id == ColumnIds.Alliance);
        var faction = UiStateDefaults.DefaultColumns.Single(column => column.Id == ColumnIds.FactionWarfare);

        Assert.True(faction.Visible);
        Assert.Equal(alliance.DisplayIndex + 1, faction.DisplayIndex);
        Assert.Equal("FW", UiText.ColumnsLabelFactionWarfare);
        Assert.Equal("FW", UiText.GridColumnHeaderFactionWarfare);
    }

    [Fact]
    public void ReconcileColumns_ExistingStateWithoutFactionWarfare_AppendsItVisibleAtEnd()
    {
        var saved = UiStateDefaults.DefaultColumns
            .Where(column => column.Id != ColumnIds.FactionWarfare)
            .ToList();

        var reconciled = UiStateDefaults.ReconcileColumns(saved);

        var faction = reconciled.Single(column => column.Id == ColumnIds.FactionWarfare);
        Assert.True(faction.Visible);
        Assert.Equal(reconciled.Count - 1, faction.DisplayIndex);
    }

    private static PilotReportRow CreateRow(long? factionId)
    {
        var pilot = new Pilot
        {
            InputName = "T'ral Vsengne",
            CharacterId = 95465499,
            CharacterName = "T'ral Vsengne",
            VerifyStatus = VerifyStatus.Partial,
            FactionId = factionId
        };

        return PilotReportRowFactory.FromPilot(pilot, null, null, StyleClassification.Solo, false, "Low", false, false, null);
    }
}

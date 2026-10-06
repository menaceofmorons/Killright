using Killright.Core.Style;
using Killright.UI.Analysis;
using Killright.UI.UiState;
using Killright.UI.ViewModels;
using Xunit;

namespace Killright.UI.Tests.ViewModels;

public sealed class GridFilterStateTests
{
    private static PilotReportRow Row(
        long id,
        long? corp = null,
        long? alliance = null,
        long? faction = null,
        StyleClassification general = StyleClassification.Unknown,
        StyleClassification recent = StyleClassification.Unknown,
        PilotRelationship[]? relationships = null) =>
        new()
        {
            CharacterId = id,
            Pilot = $"Pilot {id}",
            CorporationId = corp,
            Corporation = corp is null ? "unk" : $"Corp {corp} [2]",
            AllianceId = alliance,
            Alliance = alliance is null ? string.Empty : $"Alliance {alliance} [3]",
            FactionId = faction,
            FactionWarfare = faction is null ? "-" : "Ca (2)",
            GeneralStyleClassification = general,
            RecentStyleClassification = recent,
            GroupRelationships = relationships ?? Array.Empty<PilotRelationship>()
        };

    private static PilotRelationship Link(long a, long b, RelationshipLinkType type = RelationshipLinkType.Direct) =>
        new(a, b, type, 50, 80, null, null, null);

    private static List<long> Visible(GridFilterState state, params PilotReportRow[] rows) =>
        rows.Where(state.IsVisible).Select(row => row.CharacterId!.Value).ToList();

    [Fact]
    public void Ignore_HidesOnlyMatchingRows()
    {
        var a = Row(1, corp: 10);
        var b = Row(2, corp: 20);
        var state = new GridFilterState();

        Assert.True(state.Add(a, GridFilterField.Corporation, GridFilterMode.Ignore));

        Assert.Equal(new long[] { 2 }, Visible(state, a, b));
    }

    [Fact]
    public void FilterTo_KeepsOnlyMatchingRows()
    {
        var a = Row(1, corp: 10);
        var b = Row(2, corp: 20);
        var c = Row(3, corp: null);
        var state = new GridFilterState();

        state.Add(a, GridFilterField.Corporation, GridFilterMode.FilterTo);

        Assert.Equal(new long[] { 1 }, Visible(state, a, b, c));
    }

    [Fact]
    public void Ignore_KeepsRowsWithoutAKey()
    {
        var a = Row(1, alliance: 5);
        var b = Row(2, alliance: null);
        var state = new GridFilterState();

        state.Add(a, GridFilterField.Alliance, GridFilterMode.Ignore);

        Assert.Equal(new long[] { 2 }, Visible(state, a, b));
    }

    [Fact]
    public void TwoEntries_CombineWithAnd()
    {
        var a = Row(1, corp: 10, alliance: 5);
        var b = Row(2, corp: 20, alliance: 5);
        var c = Row(3, corp: 10, alliance: 6);
        var state = new GridFilterState();

        state.Add(a, GridFilterField.Alliance, GridFilterMode.FilterTo);
        state.Add(a, GridFilterField.Corporation, GridFilterMode.Ignore);

        Assert.Equal(new long[] { 2 }, Visible(state, a, b, c));
    }

    [Fact]
    public void CorpAllianceAndFaction_MatchOnIdNotDisplayText()
    {
        var a = Row(1, corp: 10, alliance: 5, faction: 500001);
        var b = Row(2, corp: 10, alliance: 5, faction: 500001);
        b.Corporation = "Corp 10 [9]";
        b.Alliance = "Alliance 5 [9]";
        b.FactionWarfare = "Ca (9)";
        var state = new GridFilterState();

        state.Add(a, GridFilterField.Corporation, GridFilterMode.FilterTo);
        state.Add(a, GridFilterField.Alliance, GridFilterMode.FilterTo);
        state.Add(a, GridFilterField.FactionWarfare, GridFilterMode.FilterTo);

        Assert.Equal(new long[] { 1, 2 }, Visible(state, a, b));
    }

    [Theory]
    [InlineData(StyleClassification.Solo)]
    [InlineData(StyleClassification.SoloBeginner)]
    public void GeneralStyleSolo_MatchesBeginnerAndPodderVariants_NotGang(StyleClassification variant)
    {
        var solo = Row(1, general: StyleClassification.Solo);
        var soloVariant = Row(2, general: variant);
        var gang = Row(3, general: StyleClassification.Gang);
        var state = new GridFilterState();

        state.Add(solo, GridFilterField.GeneralStyle, GridFilterMode.FilterTo);

        Assert.Equal(new long[] { 1, 2 }, Visible(state, solo, soloVariant, gang));
    }

    [Fact]
    public void GangBeginner_SharesKeyWithGang()
    {
        var gang = Row(1, recent: StyleClassification.Gang);
        var gangBeginner = Row(2, recent: StyleClassification.GangBeginner);
        var state = new GridFilterState();

        state.Add(gang, GridFilterField.RecentStyle, GridFilterMode.FilterTo);

        Assert.Equal(new long[] { 1, 2 }, Visible(state, gang, gangBeginner));
    }

    [Fact]
    public void RecentSlot_IsMatchedIndependentlyOfGeneralSlot()
    {
        var a = Row(1, general: StyleClassification.Solo, recent: StyleClassification.Gang);
        var b = Row(2, general: StyleClassification.Gang, recent: StyleClassification.Solo);
        var state = new GridFilterState();

        state.Add(a, GridFilterField.RecentStyle, GridFilterMode.FilterTo);

        Assert.Equal(new long[] { 1 }, Visible(state, a, b));
    }

    [Fact]
    public void NullValuesAndUnknownStyle_OfferNoEntry()
    {
        var row = Row(1);
        var state = new GridFilterState();

        Assert.False(GridFilterState.TryGetKey(row, GridFilterField.Alliance, out _));
        Assert.False(GridFilterState.TryGetKey(row, GridFilterField.Corporation, out _));
        Assert.False(GridFilterState.TryGetKey(row, GridFilterField.FactionWarfare, out _));
        Assert.False(GridFilterState.TryGetKey(row, GridFilterField.GeneralStyle, out _));
        Assert.False(GridFilterState.TryGetKey(row, GridFilterField.RecentStyle, out _));
        Assert.False(state.Add(row, GridFilterField.Alliance, GridFilterMode.Ignore));
        Assert.False(state.IsActive);
    }

    [Fact]
    public void DuplicateEntry_IsNotAddedTwice()
    {
        var row = Row(1, corp: 10);
        var state = new GridFilterState();

        Assert.True(state.Add(row, GridFilterField.Corporation, GridFilterMode.Ignore));
        Assert.False(state.Add(row, GridFilterField.Corporation, GridFilterMode.Ignore));
        Assert.Single(state.Entries);
    }

    [Fact]
    public void PilotFilter_VisibleSetHoldsPilotSameCorpSameAllianceDirectAndChain()
    {
        var pilot = Row(1, corp: 10, alliance: 5);
        var sameCorp = Row(2, corp: 10);
        var sameAlliance = Row(3, corp: 30, alliance: 5);
        var direct = Row(4, corp: 40, relationships: new[] { Link(4, 1) });
        var chain = Row(5, corp: 50, relationships: new[] { Link(1, 5, RelationshipLinkType.Chain) });
        var unrelated = Row(6, corp: 60);
        var state = new GridFilterState();

        var applied = state.ApplyPilotFilter(pilot, new[] { pilot, sameCorp, sameAlliance, direct, chain, unrelated }, _ => false);

        Assert.True(applied);
        Assert.Equal(new long[] { 1, 2, 3, 4, 5 }, Visible(state, pilot, sameCorp, sameAlliance, direct, chain, unrelated));
    }

    [Fact]
    public void PilotFilter_ExcludesRowSharingOnlyAnNpcCorporation()
    {
        var pilot = Row(1, corp: 1000001);
        var npcMate = Row(2, corp: 1000001);
        var state = new GridFilterState();

        state.ApplyPilotFilter(pilot, new[] { pilot, npcMate }, corporationId => corporationId == 1000001);

        Assert.Equal(new long[] { 1 }, Visible(state, pilot, npcMate));
    }

    [Fact]
    public void PilotFilter_SecondApplicationDoesNothing()
    {
        var first = Row(1, corp: 10);
        var second = Row(2, corp: 20);
        var rows = new[] { first, second };
        var state = new GridFilterState();

        Assert.True(state.ApplyPilotFilter(first, rows, _ => false));
        Assert.False(state.ApplyPilotFilter(second, rows, _ => false));
        Assert.Equal(new long[] { 1 }, Visible(state, first, second));
    }

    [Fact]
    public void PilotFilter_UnresolvedPilotIsNotApplied()
    {
        var unresolved = new PilotReportRow { CharacterId = null };
        var state = new GridFilterState();

        Assert.False(state.ApplyPilotFilter(unresolved, new[] { unresolved }, _ => false));
        Assert.False(state.IsActive);
    }

    [Fact]
    public void PilotFilter_CombinesWithTempFilterInEitherOrder()
    {
        var pilot = Row(1, corp: 10, alliance: 5);
        var mateA = Row(2, corp: 10, alliance: 5);
        var mateB = Row(3, corp: 30, alliance: 5);
        var outsider = Row(4, corp: 10, alliance: 9);
        var rows = new[] { pilot, mateA, mateB, outsider };

        var pilotFirst = new GridFilterState();
        pilotFirst.ApplyPilotFilter(pilot, rows, _ => false);
        pilotFirst.Add(mateB, GridFilterField.Corporation, GridFilterMode.Ignore);

        var tempFirst = new GridFilterState();
        tempFirst.Add(mateB, GridFilterField.Corporation, GridFilterMode.Ignore);
        tempFirst.ApplyPilotFilter(pilot, rows, _ => false);

        Assert.Equal(Visible(pilotFirst, rows), Visible(tempFirst, rows));
        Assert.Equal(new long[] { 1, 2, 4 }, Visible(pilotFirst, rows));
    }

    [Fact]
    public void Clear_RemovesEveryEntryAndThePilotFilter()
    {
        var pilot = Row(1, corp: 10);
        var other = Row(2, corp: 20);
        var state = new GridFilterState();

        state.Add(pilot, GridFilterField.Corporation, GridFilterMode.Ignore);
        state.ApplyPilotFilter(pilot, new[] { pilot, other }, _ => false);
        state.Clear();

        Assert.False(state.IsActive);
        Assert.False(state.IsPilotFilterActive);
        Assert.Equal(new long[] { 1, 2 }, Visible(state, pilot, other));
        Assert.Equal(string.Empty, state.HeaderPrefix(ColumnIds.Corporation));
        Assert.Equal(string.Empty, state.HeaderPrefix(ColumnIds.Pilot));
    }

    [Fact]
    public void HeaderPrefix_IgnoreOnlyGivesI_FilterToGivesF_BothGivesF()
    {
        var row = Row(1, corp: 10, alliance: 5, faction: 500001);
        var state = new GridFilterState();

        state.Add(row, GridFilterField.Corporation, GridFilterMode.Ignore);
        state.Add(row, GridFilterField.Alliance, GridFilterMode.FilterTo);
        state.Add(row, GridFilterField.FactionWarfare, GridFilterMode.Ignore);
        state.Add(row, GridFilterField.FactionWarfare, GridFilterMode.FilterTo);

        Assert.Equal("(I) ", state.HeaderPrefix(ColumnIds.Corporation));
        Assert.Equal("(F) ", state.HeaderPrefix(ColumnIds.Alliance));
        Assert.Equal("(F) ", state.HeaderPrefix(ColumnIds.FactionWarfare));
        Assert.Equal(string.Empty, state.HeaderPrefix(ColumnIds.Style));
    }

    [Fact]
    public void HeaderPrefix_StyleSlotsShareTheStyleColumn_PilotShowsFWhileOn()
    {
        var row = Row(1, general: StyleClassification.Solo, recent: StyleClassification.Gang);
        var state = new GridFilterState();

        state.Add(row, GridFilterField.GeneralStyle, GridFilterMode.Ignore);
        Assert.Equal("(I) ", state.HeaderPrefix(ColumnIds.Style));

        state.Add(row, GridFilterField.RecentStyle, GridFilterMode.FilterTo);
        Assert.Equal("(F) ", state.HeaderPrefix(ColumnIds.Style));

        state.ApplyPilotFilter(row, new[] { row }, _ => false);
        Assert.Equal("(F) ", state.HeaderPrefix(ColumnIds.Pilot));
        Assert.True(state.CarriesFilter(ColumnIds.Pilot));
        Assert.False(state.CarriesFilter(ColumnIds.Corporation));
    }

    [Fact]
    public void Filtering_DoesNotChangeCountsGroupOrNewPilotFlag()
    {
        var a = Row(1, corp: 10);
        a.Group = "G1";
        a.IsNewPilot = true;
        var b = Row(2, corp: 20);
        var state = new GridFilterState();

        state.Add(b, GridFilterField.Corporation, GridFilterMode.Ignore);
        _ = state.IsVisible(a);

        Assert.Equal("Corp 10 [2]", a.Corporation);
        Assert.Equal("G1", a.Group);
        Assert.True(a.IsNewPilot);
    }
}

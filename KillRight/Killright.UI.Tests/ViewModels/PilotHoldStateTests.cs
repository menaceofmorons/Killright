using Killright.UI.ViewModels;
using Xunit;

namespace Killright.UI.Tests.ViewModels;

public sealed class PilotHoldStateTests
{
    private static PilotReportRow Pilot(long id) => new() { CharacterId = id };

    [Fact]
    public void Hold_KeepsRelationshipAndHighlightOnHeldPilot_WhenHoveringElsewhere()
    {
        var state = new PilotHoldState();
        var held = Pilot(1);
        var other = Pilot(2);

        Assert.True(state.ToggleHold(held));
        state.Hover(other);

        Assert.Same(held, state.RelationshipPilot);
        Assert.Same(held, state.HighlightPilot);
    }

    [Fact]
    public void WithoutHold_HoveredPilotGoverns()
    {
        var state = new PilotHoldState();
        var other = Pilot(2);

        state.Hover(other);

        Assert.Same(other, state.RelationshipPilot);
        Assert.Same(other, state.HighlightPilot);

        state.Hover(null);

        Assert.Null(state.RelationshipPilot);
        Assert.Null(state.HighlightPilot);
    }

    [Fact]
    public void CtrlClickOnHeldPilot_Releases()
    {
        var state = new PilotHoldState();
        var held = Pilot(1);
        state.ToggleHold(held);

        Assert.False(state.ToggleHold(held));

        Assert.Null(state.Held);
        Assert.Null(state.RelationshipPilot);
    }

    [Fact]
    public void CtrlClickOnAnotherPilot_MovesHold()
    {
        var state = new PilotHoldState();
        var first = Pilot(1);
        var second = Pilot(2);
        state.ToggleHold(first);

        Assert.True(state.ToggleHold(second));

        Assert.Same(second, state.Held);
    }

    [Fact]
    public void RowWithoutCharacterId_CannotBeHeld()
    {
        var state = new PilotHoldState();

        Assert.False(state.ToggleHold(new PilotReportRow()));
        Assert.Null(state.Held);
    }

    [Fact]
    public void ReleaseHold_ClearsHold_AndKeepsFilter()
    {
        var state = new PilotHoldState();
        var filtered = Pilot(3);
        state.ToggleHold(Pilot(1));
        state.SetFiltered(filtered);

        state.ReleaseHold();

        Assert.Null(state.Held);
        Assert.Same(filtered, state.Filtered);
    }

    [Fact]
    public void Reset_OnNewScan_ReleasesHold()
    {
        var state = new PilotHoldState();
        state.ToggleHold(Pilot(1));
        state.Hover(Pilot(2));

        state.Reset();

        Assert.Null(state.Held);
        Assert.Null(state.Hovered);
        Assert.Null(state.RelationshipPilot);
    }

    [Fact]
    public void PilotFilter_OverridesHold_HighlightOff_ThenHoldReturnsWhenCleared()
    {
        var state = new PilotHoldState();
        var held = Pilot(1);
        var filtered = Pilot(3);
        state.ToggleHold(held);

        state.SetFiltered(filtered);

        Assert.Same(filtered, state.RelationshipPilot);
        Assert.Null(state.HighlightPilot);

        state.SetFiltered(null);

        Assert.Same(held, state.RelationshipPilot);
        Assert.Same(held, state.HighlightPilot);
    }
}

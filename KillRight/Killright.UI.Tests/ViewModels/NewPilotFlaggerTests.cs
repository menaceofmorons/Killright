using Killright.UI.ViewModels;
using Xunit;

namespace Killright.UI.Tests.ViewModels;

public sealed class NewPilotFlaggerTests
{
    private static PilotReportRow Row(long? characterId, string inputName) =>
        new() { CharacterId = characterId, InputName = inputName, Pilot = inputName };

    [Fact]
    public void Apply_FirstScan_FlagsNothing()
    {
        var rows = new[] { Row(1, "Lukas Naarii"), Row(2, "T'ral Vsengne") };

        var keys = NewPilotFlagger.Apply(rows, null);

        Assert.All(rows, row => Assert.False(row.IsNewPilot));
        Assert.Equal(2, keys.Count);
    }

    [Fact]
    public void Apply_ScanSharingSomePilots_FlagsOnlyPilotsAbsentFromPreviousScan()
    {
        var first = new[] { Row(1, "Lukas Naarii"), Row(2, "T'ral Vsengne") };
        var previous = NewPilotFlagger.Apply(first, null);
        var second = new[] { Row(1, "Lukas Naarii"), Row(2, "T'ral Vsengne"), Row(3, "syMptom NZ"), Row(4, "Kljucaonica") };

        NewPilotFlagger.Apply(second, previous);

        Assert.False(second[0].IsNewPilot);
        Assert.False(second[1].IsNewPilot);
        Assert.True(second[2].IsNewPilot);
        Assert.True(second[3].IsNewPilot);
    }

    [Fact]
    public void Apply_ScanSharingNoPilot_FlagsNothing()
    {
        var previous = NewPilotFlagger.Apply(new[] { Row(1, "Lukas Naarii") }, null);
        var second = new[] { Row(3, "syMptom NZ"), Row(4, "Kljucaonica") };

        NewPilotFlagger.Apply(second, previous);

        Assert.All(second, row => Assert.False(row.IsNewPilot));
    }

    [Fact]
    public void Apply_IdenticalList_FlagsNothing()
    {
        var previous = NewPilotFlagger.Apply(new[] { Row(1, "Lukas Naarii"), Row(2, "T'ral Vsengne") }, null);
        var second = new[] { Row(1, "Lukas Naarii"), Row(2, "T'ral Vsengne") };

        NewPilotFlagger.Apply(second, previous);

        Assert.All(second, row => Assert.False(row.IsNewPilot));
    }

    [Fact]
    public void Apply_UnresolvedPilot_IsKeyedByInputNameCaseInsensitively()
    {
        var previous = NewPilotFlagger.Apply(new[] { Row(1, "Lukas Naarii"), Row(null, "Fake Pilot") }, null);
        var second = new[] { Row(1, "Lukas Naarii"), Row(null, "FAKE PILOT"), Row(null, "Other Fake") };

        NewPilotFlagger.Apply(second, previous);

        Assert.False(second[1].IsNewPilot);
        Assert.True(second[2].IsNewPilot);
    }

    [Fact]
    public void Apply_ReturnsKeysOfCurrentRowsForTheNextScan()
    {
        var first = NewPilotFlagger.Apply(new[] { Row(1, "Lukas Naarii"), Row(2, "T'ral Vsengne") }, null);
        var second = NewPilotFlagger.Apply(new[] { Row(2, "T'ral Vsengne"), Row(3, "syMptom NZ") }, first);
        var third = new[] { Row(1, "Lukas Naarii"), Row(3, "syMptom NZ") };

        NewPilotFlagger.Apply(third, second);

        Assert.True(third[0].IsNewPilot);
        Assert.False(third[1].IsNewPilot);
    }
}

using Killright.Core.Models;
using Killright.Shared;
using Killright.UI.Scan;
using Killright.UI.UiState;
using Xunit;

namespace Killright.UI.Tests.Scan;

public sealed class ScanPilotSelectorTests
{
    private static readonly Pilot Tral = new()
    {
        InputName = "T'ral Vsengne",
        CharacterId = 95465499,
        VerifyStatus = VerifyStatus.Partial,
        Corporation = new Corporation { CorporationId = 98765, Name = "Test Corp" }
    };

    private static readonly Pilot Lukas = new()
    {
        InputName = "Lukas Naarii",
        CharacterId = 91321792,
        VerifyStatus = VerifyStatus.Partial,
        Corporation = new Corporation { CorporationId = 98766, Name = "Other Corp" }
    };

    [Fact]
    public void SelectForAnalysis_NoMatchAndFailedPilots_AreDropped()
    {
        var pilots = new[]
        {
            Tral,
            new Pilot { InputName = "syMptom NZ", VerifyStatus = VerifyStatus.NoMatch },
            new Pilot { InputName = "Broken", VerifyStatus = VerifyStatus.Failed },
            Lukas
        };

        var selected = ScanPilotSelector.SelectForAnalysis(pilots, []);

        Assert.Equal([Tral, Lukas], selected);
    }

    [Fact]
    public void SelectForAnalysis_IgnoredPilotCorporationAndAlliance_AreDroppedBeforeAnyNetworkStage()
    {
        var allianceMember = Lukas with { AllianceId = 99001 };
        var ignoreList = new[]
        {
            new IgnoreListEntry { Id = 95465499, Type = IgnoreEntryType.Pilot },
            new IgnoreListEntry { Id = 99001, Type = IgnoreEntryType.Alliance }
        };

        var selected = ScanPilotSelector.SelectForAnalysis([Tral, allianceMember], ignoreList);

        Assert.Empty(selected);
    }

    [Fact]
    public void SelectForAnalysis_KeepsListOrder()
    {
        var selected = ScanPilotSelector.SelectForAnalysis([Lukas, Tral], []);

        Assert.Equal([Lukas, Tral], selected);
    }
}

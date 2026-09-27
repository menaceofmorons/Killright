using Killright.Core.Models;
using Killright.Shared;
using Killright.UI.UiState;
using Xunit;

namespace Killright.UI.Tests;

public sealed class IgnoreListFilterTests
{
    private static Pilot CreatePilot(long characterId, long? corporationId = null, long? allianceId = null)
    {
        return new Pilot
        {
            InputName = "T'ral Vsengne",
            CharacterId = characterId,
            VerifyStatus = VerifyStatus.Partial,
            Corporation = corporationId is null ? null : new Corporation { CorporationId = corporationId.Value, Name = "Test Corp" },
            Alliance = allianceId is null ? null : new Alliance { AllianceId = allianceId.Value, Name = "Test Alliance" },
            AllianceId = allianceId
        };
    }

    [Fact]
    public void IsIgnored_EmptyList_ReturnsFalse()
    {
        var pilot = CreatePilot(95465499);

        Assert.False(IgnoreListFilter.IsIgnored(pilot, Array.Empty<IgnoreListEntry>()));
    }

    [Fact]
    public void IsIgnored_CharacterIdMatchesPilotEntry_ReturnsTrue()
    {
        var pilot = CreatePilot(95465499);
        var ignoreList = new[] { new IgnoreListEntry { Id = 95465499, Type = IgnoreEntryType.Pilot, Name = "T'ral Vsengne" } };

        Assert.True(IgnoreListFilter.IsIgnored(pilot, ignoreList));
    }

    [Fact]
    public void IsIgnored_CorporationIdMatchesCorporationEntry_ReturnsTrue()
    {
        var pilot = CreatePilot(95465499, corporationId: 98765);
        var ignoreList = new[] { new IgnoreListEntry { Id = 98765, Type = IgnoreEntryType.Corporation, Name = "Test Corp" } };

        Assert.True(IgnoreListFilter.IsIgnored(pilot, ignoreList));
    }

    [Fact]
    public void IsIgnored_AllianceIdMatchesAllianceEntry_ReturnsTrue()
    {
        var pilot = CreatePilot(95465499, allianceId: 99001);
        var ignoreList = new[] { new IgnoreListEntry { Id = 99001, Type = IgnoreEntryType.Alliance, Name = "Test Alliance" } };

        Assert.True(IgnoreListFilter.IsIgnored(pilot, ignoreList));
    }

    [Fact]
    public void IsIgnored_CorporationEntryDoesNotMatchOnCharacterId_ReturnsFalse()
    {
        var pilot = CreatePilot(95465499, corporationId: 98765);
        var ignoreList = new[] { new IgnoreListEntry { Id = 95465499, Type = IgnoreEntryType.Corporation, Name = "Coincidental Id" } };

        Assert.False(IgnoreListFilter.IsIgnored(pilot, ignoreList));
    }

    [Fact]
    public void IsIgnored_NoCorporationResolved_DoesNotMatchCorporationEntry()
    {
        var pilot = CreatePilot(95465499);
        var ignoreList = new[] { new IgnoreListEntry { Id = 98765, Type = IgnoreEntryType.Corporation, Name = "Test Corp" } };

        Assert.False(IgnoreListFilter.IsIgnored(pilot, ignoreList));
    }
}

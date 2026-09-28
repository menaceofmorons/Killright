using Killright.Shared.Killmails;
using Xunit;

namespace Killright.Storage.Tests;

public sealed class KillmailQualificationTests
{
    [Fact]
    public void CountUniqueAttackers_DuplicateCharacterIds_CountsOnce()
    {
        var attackers = new List<KillmailAttacker>
        {
            new(95465499, 98000001, null, 587),
            new(95465499, 98000001, null, 587),
            new(91321792, 98000002, null, 11567)
        };

        var count = KillmailQualification.CountUniqueAttackers(attackers);

        Assert.Equal(2, count);
    }

    [Fact]
    public void CountUniqueAttackers_AttackerWithoutCharacterId_Excluded()
    {
        var attackers = new List<KillmailAttacker>
        {
            new(95465499, 98000001, null, 587),
            new(null, 1000125, null, 32872)
        };

        var count = KillmailQualification.CountUniqueAttackers(attackers);

        Assert.Equal(1, count);
    }

    [Theory]
    [InlineData(670L, true)]
    [InlineData(33328L, true)]
    [InlineData(587L, false)]
    [InlineData(null, false)]
    public void IsPodKill_MatchesCapsuleShipTypeIdsOnly(long? shipTypeId, bool expected)
    {
        Assert.Equal(expected, KillmailQualification.IsPodKill(shipTypeId));
    }

    [Theory]
    [InlineData(1, false, 11, false)]
    [InlineData(2, false, 11, true)]
    [InlineData(10, false, 11, true)]
    [InlineData(11, false, 11, false)]
    [InlineData(2, true, 11, false)]
    [InlineData(4, false, 5, true)]
    [InlineData(5, false, 5, false)]
    public void IsQualifying_AppliesAttackerCountPodRulesAndConfiguredThreshold(int uniqueAttackerCount, bool isPodKill, int fleetThreshold, bool expected)
    {
        Assert.Equal(expected, KillmailQualification.IsQualifying(uniqueAttackerCount, isPodKill, fleetThreshold));
    }
}

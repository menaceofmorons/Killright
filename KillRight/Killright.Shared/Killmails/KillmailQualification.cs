namespace Killright.Shared.Killmails;

public static class KillmailQualification
{
    public const int FleetThreshold = 11;
    public const long CapsuleShipTypeId = 670;
    public const long CapsuleGenolutionShipTypeId = 33328;

    private static readonly HashSet<long> PodShipTypeIds = [CapsuleShipTypeId, CapsuleGenolutionShipTypeId];

    public static int CountUniqueAttackers(IReadOnlyList<KillmailAttacker> attackers)
    {
        var unique = new HashSet<long>();

        foreach (var attacker in attackers)
        {
            if (attacker.CharacterId is long characterId)
                unique.Add(characterId);
        }

        return unique.Count;
    }

    public static bool IsPodKill(long? victimShipTypeId)
    {
        return victimShipTypeId is long shipTypeId && PodShipTypeIds.Contains(shipTypeId);
    }

    public static bool IsQualifying(int uniqueAttackerCount, bool isPodKill)
    {
        return uniqueAttackerCount >= 2 && uniqueAttackerCount < FleetThreshold && !isPodKill;
    }
}

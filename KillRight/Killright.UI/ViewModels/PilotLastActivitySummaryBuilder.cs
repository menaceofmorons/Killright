using Killright.Shared.zKill;
using Killright.Storage.Killmails;
using Killright.Storage.Sde;

namespace Killright.UI.ViewModels;

public static class PilotLastActivitySummaryBuilder
{
    public static PilotLastActivitySummary? Build(
        PilotRecentKillmail? lastActivity,
        ISdeReferenceDataStore? sdeStore)
    {
        if (lastActivity is null)
            return null;

        var isKill = lastActivity.ActivityType == zKillActivityType.Kill;

        return new PilotLastActivitySummary
        {
            DateTime = lastActivity.KillTimeUtc.UtcDateTime.ToString("yyyy-MM-dd HH:mm"),
            KillLoss = isKill ? "Kill" : "Loss",
            System = ResolveSystemName(sdeStore, lastActivity.SystemId),
            Ship = ResolveTypeName(sdeStore, lastActivity.ShipTypeId),
            Weapon = isKill ? ResolveTypeName(sdeStore, lastActivity.WeaponTypeId) : "—",
            Victim = isKill ? ResolveTypeName(sdeStore, lastActivity.VictimShipTypeId) : "—",
            Attackers = isKill ? lastActivity.AttackerCount?.ToString() ?? "—" : "—",
            IsKill = isKill
        };
    }

    private static string ResolveTypeName(ISdeReferenceDataStore? sdeStore, long? typeId)
    {
        if (sdeStore is null || typeId is null)
            return "—";

        try
        {
            return sdeStore.GetTypeName(typeId.Value) ?? "—";
        }
        catch
        {
            return "—";
        }
    }

    private static string ResolveSystemName(ISdeReferenceDataStore? sdeStore, long systemId)
    {
        if (sdeStore is null)
            return "—";

        try
        {
            return sdeStore.GetSolarSystemName(systemId) ?? "—";
        }
        catch
        {
            return "—";
        }
    }
}

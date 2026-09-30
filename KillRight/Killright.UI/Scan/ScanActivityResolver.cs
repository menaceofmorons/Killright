using Killright.Core.Activity;
using Killright.Integration.zKill;
using Killright.Shared.zKill;

namespace Killright.UI.Scan;

public sealed record ActivityResolution(zKillActivity? Activity, bool Persist);

public static class ScanActivityResolver
{
    public static ActivityResolution Resolve(
        long characterId,
        zKillStatistics? statistics,
        zKillActivity? stored,
        zKillActivity? killmailDerived,
        DateTimeOffset? newLastSuccessfulCallUtc,
        int? pastSecondsRequested,
        DateTimeOffset now)
    {
        try
        {
            var (statisticsLastActiveUtc, statisticsActivityType) =
                LastActiveDeriver.Derive(statistics?.months, now);

            var lastActiveUtc = stored?.LastActiveUtc;
            var lastActivityType = stored?.LastActivityType;

            if (statisticsLastActiveUtc is not null && (lastActiveUtc is null || statisticsLastActiveUtc > lastActiveUtc))
            {
                lastActiveUtc = statisticsLastActiveUtc;
                lastActivityType = statisticsActivityType;
            }

            if (killmailDerived?.LastActiveUtc is not null && (lastActiveUtc is null || killmailDerived.LastActiveUtc > lastActiveUtc))
            {
                lastActiveUtc = killmailDerived.LastActiveUtc;
                lastActivityType = killmailDerived.LastActivityType;
            }

            var lastSuccessfulRecentCallUtc = newLastSuccessfulCallUtc ?? stored?.LastSuccessfulRecentCallUtc;
            var recentCoverageStartUtc = RecentCallScheduler.ResolveCoverageStartUtc(
                stored?.RecentCoverageStartUtc,
                stored?.LastSuccessfulRecentCallUtc,
                pastSecondsRequested,
                now);

            var hasPublicActivityData = lastActiveUtc is not null
                || (stored?.HasPublicActivityData ?? false)
                || (killmailDerived?.HasPublicActivityData ?? false);

            if (stored is null && !hasPublicActivityData && lastSuccessfulRecentCallUtc is null)
                return new ActivityResolution(killmailDerived, false);

            var merged = new zKillActivity(
                characterId,
                hasPublicActivityData,
                killmailDerived?.KillsWeek ?? stored?.KillsWeek,
                killmailDerived?.SoloWeek ?? stored?.SoloWeek,
                lastActiveUtc,
                lastActivityType,
                now,
                killmailDerived?.Error,
                lastSuccessfulRecentCallUtc,
                recentCoverageStartUtc,
                killmailDerived?.LastKillUtc);

            return new ActivityResolution(merged, true);
        }
        catch
        {
            return new ActivityResolution(killmailDerived, false);
        }
    }
}

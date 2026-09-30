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

            var lastKillUtc = LatestOf(
                stored?.LastKillUtc,
                LastActiveDeriver.DeriveLastKill(statistics?.months, now),
                killmailDerived?.LastKillUtc);

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
                lastKillUtc);

            return new ActivityResolution(merged, true);
        }
        catch
        {
            return new ActivityResolution(killmailDerived, false);
        }
    }

    private static DateTimeOffset? LatestOf(params DateTimeOffset?[] candidates)
    {
        DateTimeOffset? latest = null;

        foreach (var candidate in candidates)
        {
            if (candidate is not null && (latest is null || candidate > latest))
                latest = candidate;
        }

        return latest;
    }
}

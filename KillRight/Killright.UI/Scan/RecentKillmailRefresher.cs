using Killright.Core.Activity;
using Killright.Integration.zKill;
using Killright.Shared.zKill;
using Killright.Storage.Diagnostics;
using Killright.Storage.Scan;

namespace Killright.UI.Scan;

public readonly record struct RecentRefreshOutcome(DateTimeOffset? NewLastSuccessfulCallUtc, bool Failed, int? PastSecondsRequested);

public static class RecentKillmailRefresher
{
    public static async Task<RecentRefreshOutcome> RefreshAsync(
        IzKillClient client,
        long characterId,
        int position,
        zKillActivity? storedActivity,
        zKillStatistics? statistics,
        bool statisticsFetchedThisScan,
        ScanWriteBatch writes,
        DateTimeOffset now,
        ScanTimings? timings,
        CancellationToken cancellationToken)
    {
        try
        {
            var lastSuccessfulCallUtc = storedActivity?.LastSuccessfulRecentCallUtc;

            if (RecentCallScheduler.ShouldSkipForInterval(lastSuccessfulCallUtc, now))
                return new RecentRefreshOutcome(null, false, null);

            if (statistics?.NoHistory ?? false)
                return new RecentRefreshOutcome(now, false, null);

            if (statisticsFetchedThisScan && RecentCallScheduler.ShouldShortCircuit(statistics?.months, now))
                return new RecentRefreshOutcome(now, false, null);

            var pastSeconds = RecentCallScheduler.CalculatePastSeconds(lastSuccessfulCallUtc, now);
            zKillRecentKillmailResult result;

            using (timings.Measure(ScanTimings.PilotLevel, "zkill_http", characterId))
            {
                result = await client.GetRecentKillmailsAsync(characterId, pastSeconds, cancellationToken);
            }

            switch (result.Outcome)
            {
                case zKillRecentKillmailOutcome.Success:
                    if (result.RawKillmails.Count > 0)
                    {
                        writes.AddKillmails(position, characterId, result.RawKillmails);
                        writes.AddNoHistoryClear(characterId);
                    }

                    return new RecentRefreshOutcome(now, false, pastSeconds);

                case zKillRecentKillmailOutcome.NoHistory:
                    writes.AddNoHistoryMark(characterId);
                    return new RecentRefreshOutcome(now, false, pastSeconds);

                default:
                    return new RecentRefreshOutcome(null, true, null);
            }
        }
        catch
        {
            return new RecentRefreshOutcome(null, true, null);
        }
    }
}

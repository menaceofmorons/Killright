namespace Killright.UI.Diagnostics;

public sealed class DeveloperCacheOperations(DiagnosticsDataService service)
{
    public void ClearIdentityCache()
    {
        service.ExecuteNonQuery("DELETE FROM main.pilot_identity_cache;");
    }

    public void ClearActivityCache()
    {
        service.ExecuteNonQuery("""
            DELETE FROM main.zkill_activity_cache
            WHERE last_recent_call_utc IS NULL
              AND recent_coverage_start_utc IS NULL;
            """);
        service.ExecuteNonQuery("""
            UPDATE main.zkill_activity_cache
            SET last_active_utc = NULL,
                last_activity_type = NULL,
                last_kill_utc = NULL,
                kills_week = NULL,
                solo_week = NULL,
                error = NULL,
                checked_at_utc = 0;
            """);
        service.ExecuteNonQuery("DELETE FROM main.pilot_last_killmail_cache;");
        service.ExecuteNonQuery("UPDATE main.zkill_statistics_cache SET months_processed = NULL;");
    }

    public void ClearKillmailCache()
    {
        service.ExecuteNonQuery("""
            DELETE FROM main.zkill_killmail_attackers
            WHERE killmail_id IN (
                SELECT killmail_id
                FROM main.zkill_killmails
                WHERE is_qualifying = 0
            );
            """);
        service.ExecuteNonQuery("DELETE FROM main.zkill_killmails WHERE is_qualifying = 0;");
        service.ExecuteNonQuery("UPDATE main.zkill_activity_cache SET last_recent_call_utc = NULL;");
    }

    public void ClearStatisticsCache()
    {
        service.ExecuteNonQuery("DELETE FROM main.zkill_statistics_cache;");
    }
}

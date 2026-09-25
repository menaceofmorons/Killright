using System.Data;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using Killright.Shared.Time;

namespace Killright.UI.Diagnostics;

public partial class DiagnosticsView : UserControl
{
    private static readonly Dictionary<string, string> DiagnosticQueries = new()
    {
        ["Pilot Refresh Status"] = """
            SELECT character_id,
                   MAX(kill_time_utc) AS latest_killmail_utc,
                   MAX(cached_at_utc) AS latest_cache_write_utc,
                   COUNT(*) AS cached_killmails
            FROM (
                SELECT victim_character_id AS character_id, kill_time_utc, cached_at_utc
                FROM main.zkill_killmails
                WHERE victim_character_id IS NOT NULL
                UNION ALL
                SELECT a.character_id, k.kill_time_utc, k.cached_at_utc
                FROM main.zkill_killmail_attackers a
                JOIN main.zkill_killmails k ON k.killmail_id = a.killmail_id
            ) combined
            GROUP BY character_id
            ORDER BY latest_killmail_utc DESC;
            """,

        ["Latest Killmail Per Pilot"] = """
            SELECT character_id,
                   MAX(kill_time_utc) AS latest_killmail_utc
            FROM (
                SELECT victim_character_id AS character_id, kill_time_utc
                FROM main.zkill_killmails
                WHERE victim_character_id IS NOT NULL
                UNION ALL
                SELECT a.character_id, k.kill_time_utc
                FROM main.zkill_killmail_attackers a
                JOIN main.zkill_killmails k ON k.killmail_id = a.killmail_id
            ) combined
            GROUP BY character_id
            ORDER BY latest_killmail_utc DESC;
            """,

        ["Killmail Count Per Pilot"] = """
            SELECT character_id,
                   COUNT(*) AS cached_killmails
            FROM (
                SELECT victim_character_id AS character_id
                FROM main.zkill_killmails
                WHERE victim_character_id IS NOT NULL
                UNION ALL
                SELECT character_id
                FROM main.zkill_killmail_attackers
            ) combined
            GROUP BY character_id
            ORDER BY character_id;
            """,

        ["Duplicate Killmail Check"] = """
            SELECT killmail_id,
                   COUNT(*) AS duplicate_count
            FROM main.zkill_killmails
            GROUP BY killmail_id
            HAVING COUNT(*) > 1;
            """,

        ["Expired Killmail Check"] = """
            SELECT *
            FROM main.zkill_killmails
            WHERE is_qualifying = FALSE
              AND kill_time_utc < '{{RecentWindowCutoffUtc}}'
            ORDER BY kill_time_utc DESC;
            """,

        ["Recent Killmail Rows"] = """
            SELECT *
            FROM main.zkill_killmails
            ORDER BY kill_time_utc DESC
            LIMIT 100;
            """,

        ["Ship Usage Check"] = """
            SELECT killmail_id,
                   victim_character_id,
                   is_qualifying,
                   victim_ship_type_id,
                   unique_attacker_count,
                   is_solo,
                   kill_time_utc
            FROM main.zkill_killmails
            WHERE victim_ship_type_id IS NOT NULL
            ORDER BY kill_time_utc DESC
            LIMIT 100;
            """,

        ["zKill Statistics Cache Rows"] = """
            SELECT *
            FROM main.zkill_statistics_cache
            ORDER BY checked_at_utc DESC;
            """,

        ["zKill Statistics Cache Count"] = """
            SELECT COUNT(*) AS statistics_cache_rows
            FROM main.zkill_statistics_cache;
            """,

        ["Expired zKill Statistics Cache Rows"] = """
            SELECT *
            FROM main.zkill_statistics_cache
            WHERE checked_at_utc < CAST((CURRENT_TIMESTAMP - INTERVAL '30 days') AS TEXT)
            ORDER BY checked_at_utc DESC;
            """,

        ["Activity Cache Rows"] = """
            SELECT *
            FROM main.zkill_activity_cache
            ORDER BY checked_at_utc DESC
            LIMIT 100;
            """,

        ["Identity Cache Rows"] = """
            SELECT *
            FROM main.pilot_identity_cache
            ORDER BY cached_at_utc DESC
            LIMIT 100;
            """,

        ["Group Detection: Pairs By Shared Kill Count"] = """
            SELECT LEAST(a.character_id, b.character_id) AS pilot_a,
                   GREATEST(a.character_id, b.character_id) AS pilot_b,
                   COUNT(DISTINCT a.killmail_id) AS shared_kills,
                   MAX(k.kill_time_utc) AS last_shared_kill_utc
            FROM main.zkill_killmail_attackers a
            JOIN main.zkill_killmail_attackers b
              ON a.killmail_id = b.killmail_id AND a.character_id < b.character_id
            JOIN main.zkill_killmails k ON k.killmail_id = a.killmail_id
            GROUP BY pilot_a, pilot_b
            ORDER BY shared_kills DESC
            LIMIT 200;
            """,

        ["Group Detection: After-Split Candidate Pairs"] = """
            WITH shared AS (
                SELECT LEAST(a.character_id, b.character_id) AS pilot_a,
                       GREATEST(a.character_id, b.character_id) AS pilot_b,
                       k.kill_time_utc,
                       (a.alliance_id IS NOT NULL AND a.alliance_id = b.alliance_id)
                           OR (a.corporation_id IS NOT NULL AND a.corporation_id = b.corporation_id) AS is_same_corp_or_alliance
                FROM main.zkill_killmail_attackers a
                JOIN main.zkill_killmail_attackers b
                  ON a.killmail_id = b.killmail_id AND a.character_id < b.character_id
                JOIN main.zkill_killmails k ON k.killmail_id = a.killmail_id
            )
            SELECT pilot_a,
                   pilot_b,
                   MIN(CASE WHEN is_same_corp_or_alliance THEN kill_time_utc END) AS earliest_same_corp_kill_utc,
                   MAX(CASE WHEN NOT is_same_corp_or_alliance THEN kill_time_utc END) AS latest_not_same_kill_utc
            FROM shared
            GROUP BY pilot_a, pilot_b
            HAVING MIN(CASE WHEN is_same_corp_or_alliance THEN kill_time_utc END) IS NOT NULL
               AND MAX(CASE WHEN NOT is_same_corp_or_alliance THEN kill_time_utc END) >
                   MIN(CASE WHEN is_same_corp_or_alliance THEN kill_time_utc END)
            ORDER BY latest_not_same_kill_utc DESC
            LIMIT 200;
            """,

        ["Group Detection: Gated-Out Pairs (Current Same Corp/Alliance)"] = """
            WITH shared AS (
                SELECT LEAST(a.character_id, b.character_id) AS pilot_a,
                       GREATEST(a.character_id, b.character_id) AS pilot_b,
                       COUNT(DISTINCT a.killmail_id) AS shared_kills
                FROM main.zkill_killmail_attackers a
                JOIN main.zkill_killmail_attackers b
                  ON a.killmail_id = b.killmail_id AND a.character_id < b.character_id
                GROUP BY pilot_a, pilot_b
            )
            SELECT s.pilot_a,
                   s.pilot_b,
                   s.shared_kills,
                   ia.corporation_id AS pilot_a_corporation_id,
                   ia.alliance_id AS pilot_a_alliance_id,
                   ib.corporation_id AS pilot_b_corporation_id,
                   ib.alliance_id AS pilot_b_alliance_id
            FROM shared s
            JOIN main.pilot_identity_cache ia ON ia.character_id = s.pilot_a
            JOIN main.pilot_identity_cache ib ON ib.character_id = s.pilot_b
            WHERE (ia.alliance_id IS NOT NULL AND ia.alliance_id = ib.alliance_id)
               OR (ia.corporation_id IS NOT NULL AND ia.corporation_id = ib.corporation_id)
            ORDER BY s.shared_kills DESC
            LIMIT 200;
            """,

        ["Group Detection: Unscanned Hub Candidates"] = """
            WITH neighbors AS (
                SELECT a.character_id AS intermediary_character_id,
                       b.character_id AS neighbor_character_id
                FROM main.zkill_killmail_attackers a
                JOIN main.zkill_killmail_attackers b
                  ON a.killmail_id = b.killmail_id AND a.character_id <> b.character_id
            )
            SELECT n.intermediary_character_id,
                   COUNT(DISTINCT n.neighbor_character_id) AS distinct_neighbors
            FROM neighbors n
            LEFT JOIN main.pilot_identity_cache i ON i.character_id = n.intermediary_character_id
            WHERE i.character_id IS NULL
            GROUP BY n.intermediary_character_id
            HAVING COUNT(DISTINCT n.neighbor_character_id) >= 2
            ORDER BY distinct_neighbors DESC
            LIMIT 200;
            """,

        ["Group Detection: Pairs With Multiple Intermediary Candidates"] = """
            WITH neighbors AS (
                SELECT a.character_id AS intermediary_character_id,
                       b.character_id AS neighbor_character_id
                FROM main.zkill_killmail_attackers a
                JOIN main.zkill_killmail_attackers b
                  ON a.killmail_id = b.killmail_id AND a.character_id <> b.character_id
            ),
            hubs AS (
                SELECT intermediary_character_id
                FROM neighbors
                GROUP BY intermediary_character_id
                HAVING COUNT(DISTINCT neighbor_character_id) >= 2
            ),
            pairs AS (
                SELECT n1.intermediary_character_id,
                       LEAST(n1.neighbor_character_id, n2.neighbor_character_id) AS pilot_a,
                       GREATEST(n1.neighbor_character_id, n2.neighbor_character_id) AS pilot_b
                FROM neighbors n1
                JOIN neighbors n2
                  ON n1.intermediary_character_id = n2.intermediary_character_id
                 AND n1.neighbor_character_id < n2.neighbor_character_id
                WHERE n1.intermediary_character_id IN (SELECT intermediary_character_id FROM hubs)
            )
            SELECT pilot_a,
                   pilot_b,
                   COUNT(DISTINCT intermediary_character_id) AS distinct_intermediary_candidates
            FROM pairs
            GROUP BY pilot_a, pilot_b
            HAVING COUNT(DISTINCT intermediary_character_id) >= 2
            ORDER BY distinct_intermediary_candidates DESC
            LIMIT 200;
            """,

        ["Group Detection: Top Hub Candidates"] = """
            SELECT a.character_id AS intermediary_character_id,
                   COUNT(DISTINCT b.character_id) AS distinct_neighbors
            FROM main.zkill_killmail_attackers a
            JOIN main.zkill_killmail_attackers b
              ON a.killmail_id = b.killmail_id AND a.character_id <> b.character_id
            GROUP BY a.character_id
            ORDER BY distinct_neighbors DESC
            LIMIT 50;
            """
    };

    private readonly DiagnosticsDataService _service;
    private readonly EngineDiagnosticsClient _engineDiagnosticsClient;
    private DataTable _lastQueryRows = new();

    public DiagnosticsView()
    {
        InitializeComponent();
        _service = new DiagnosticsDataService(App.Database);
        _engineDiagnosticsClient = new EngineDiagnosticsClient(App.EngineRuntime);

        QuerySelector.ItemsSource = DiagnosticQueries.Keys;
        QuerySelector.SelectedIndex = 0;

        RefreshAll();
        RunSelectedQuery();
    }

    private void RefreshAll()
    {
        var summary = _service.LoadSummary();

        ClockText.Text =
            $"Current UTC:   {summary.CurrentUtc:yyyy-MM-dd HH:mm:ss} UTC\n" +
            $"Effective UTC: {summary.EffectiveUtc:yyyy-MM-dd HH:mm:ss} UTC\n" +
            $"Offset Days:   {summary.OffsetDays:+#;-#;0}";

        SummaryText.Text =
            $"Identity Cache Rows:      {summary.IdentityCacheRows}\n" +
            $"Activity Cache Rows:      {summary.ActivityCacheRows}\n" +
            $"Recent Killmail Rows:     {summary.RecentKillmailRows}\n" +
            $"Duplicate Killmail Rows:  {summary.DuplicateKillmailRows}\n" +
            $"Expired Killmail Rows:    {summary.ExpiredKillmailRows}\n" +
            $"Schema Version:           {summary.SchemaVersion}\n" +
            $"Alpha Release Locked:     {summary.AlphaReleaseSchemaLocked}";

        IdentityGrid.ItemsSource = _service.LoadRows("""
            SELECT *
            FROM main.pilot_identity_cache
            ORDER BY cached_at_utc DESC;
            """).DefaultView;

        ActivityGrid.ItemsSource = _service.LoadRows("""
            SELECT *
            FROM main.zkill_activity_cache
            ORDER BY checked_at_utc DESC;
            """).DefaultView;

        KillmailGrid.ItemsSource = _service.LoadRows("""
            SELECT *
            FROM main.zkill_killmails
            ORDER BY kill_time_utc DESC
            LIMIT 500;
            """).DefaultView;

        DiagnosticsText.Text = BuildDiagnosticsText(summary);
    }

    private static string BuildDiagnosticsText(DiagnosticsSummary summary)
    {
        return $"""
               Diagnostic Summary
               ------------------
               Current UTC:              {summary.CurrentUtc:yyyy-MM-dd HH:mm:ss} UTC
               Effective UTC:            {summary.EffectiveUtc:yyyy-MM-dd HH:mm:ss} UTC
               Offset Days:              {summary.OffsetDays:+#;-#;0}

               Identity Cache Rows:      {summary.IdentityCacheRows}
               Activity Cache Rows:      {summary.ActivityCacheRows}
               Recent Killmail Rows:     {summary.RecentKillmailRows}
               Duplicate Killmail Rows:  {summary.DuplicateKillmailRows}
               Expired Killmail Rows:    {summary.ExpiredKillmailRows}
               Schema Version:           {summary.SchemaVersion}
               Alpha Release Locked:     {summary.AlphaReleaseSchemaLocked}
               """;
    }

    private void RunSelectedQuery()
    {
        if (QuerySelector.SelectedItem is not string selected)
            return;

        if (!DiagnosticQueries.TryGetValue(selected, out var sql))
            return;

        sql = sql.Replace("{{RecentWindowCutoffUtc}}", ApplicationClock.UtcNow.AddDays(-14).UtcDateTime.ToString("O"));

        QueryText.Text = sql;
        _lastQueryRows = _service.LoadRows(sql);
        QueryGrid.ItemsSource = _lastQueryRows.DefaultView;
    }

    private void Refresh_Click(object sender, RoutedEventArgs e)
    {
        RefreshAll();
        RunSelectedQuery();
    }

    private void RunQuery_Click(object sender, RoutedEventArgs e)
    {
        RunSelectedQuery();
    }

    private void CopyQueryResults_Click(object sender, RoutedEventArgs e)
    {
        Clipboard.SetText(BuildQueryResultText());
    }

    private string BuildQueryResultText()
    {
        if (QuerySelector.SelectedItem is not string selected)
            selected = "Diagnostics Query";

        var builder = new StringBuilder();
        builder.AppendLine(selected);
        builder.AppendLine(new string('-', selected.Length));
        builder.AppendLine(QueryText.Text.Trim());
        builder.AppendLine();

        if (_lastQueryRows.Rows.Count == 0)
        {
            builder.AppendLine("0 rows");
            return builder.ToString();
        }

        var columns = _lastQueryRows.Columns
            .Cast<DataColumn>()
            .Select(column => column.ColumnName)
            .ToList();

        builder.AppendLine(string.Join("\t", columns));

        foreach (DataRow row in _lastQueryRows.Rows)
        {
            builder.AppendLine(
                string.Join("\t",
                    columns.Select(column => row[column]?.ToString())));
        }

        return builder.ToString();
    }

    private async void RunGroupDetectionDiagnostics_Click(object sender, RoutedEventArgs e)
    {
        var scannedCharacterIds = ParseCharacterIds(GroupDetectionScanSetText.Text);

        if (scannedCharacterIds.Count == 0)
        {
            MessageBox.Show(
                "Enter one or more scanned character IDs.",
                "KillRight Developer Diagnostics",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var result = await _engineDiagnosticsClient.DiagnoseGroupDetectionAsync(scannedCharacterIds);

        DirectRelationshipsGrid.ItemsSource = result.DirectRelationships.DefaultView;
        ChainsGrid.ItemsSource = result.Chains.DefaultView;
        HubsGrid.ItemsSource = result.Hubs.DefaultView;

        GroupDetectionTimingText.Text =
            $"Direct Analysis:  {result.DirectAnalysisDurationMs} ms\n" +
            $"Chain Analysis:   {result.ChainAnalysisDurationMs} ms";
    }

    private async void RunThreatDiagnostics_Click(object sender, RoutedEventArgs e)
    {
        if (!long.TryParse(ThreatDiagnosticsCharacterIdText.Text.Trim(), out var characterId))
        {
            MessageBox.Show(
                "Enter a numeric character ID.",
                "KillRight Developer Diagnostics",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var table = await _engineDiagnosticsClient.DiagnoseThreatAsync(characterId);
        ThreatBreakdownGrid.ItemsSource = table.DefaultView;
    }

    private static List<long> ParseCharacterIds(string text)
    {
        return text
            .Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(token => long.TryParse(token, out var value) ? value : (long?)null)
            .Where(value => value.HasValue)
            .Select(value => value!.Value)
            .ToList();
    }

    private void Minus30_Click(object sender, RoutedEventArgs e)
    {
        ApplicationClock.AddDays(-30);
        RefreshAll();
        RunSelectedQuery();
    }

    private void Minus7_Click(object sender, RoutedEventArgs e)
    {
        ApplicationClock.AddDays(-7);
        RefreshAll();
        RunSelectedQuery();
    }

    private void Minus1_Click(object sender, RoutedEventArgs e)
    {
        ApplicationClock.AddDays(-1);
        RefreshAll();
        RunSelectedQuery();
    }

    private void Plus1_Click(object sender, RoutedEventArgs e)
    {
        ApplicationClock.AddDays(1);
        RefreshAll();
        RunSelectedQuery();
    }

    private void Plus7_Click(object sender, RoutedEventArgs e)
    {
        ApplicationClock.AddDays(7);
        RefreshAll();
        RunSelectedQuery();
    }

    private void Plus30_Click(object sender, RoutedEventArgs e)
    {
        ApplicationClock.AddDays(30);
        RefreshAll();
        RunSelectedQuery();
    }

    private void ResetClock_Click(object sender, RoutedEventArgs e)
    {
        ApplicationClock.Reset();
        RefreshAll();
        RunSelectedQuery();
    }

    private async void RemoveExpired_Click(object sender, RoutedEventArgs e)
    {
        await App.RecentKillmailCache.RemoveExpiredAsync();
        RefreshAll();
        RunSelectedQuery();
    }

    private void ClearIdentity_Click(object sender, RoutedEventArgs e)
    {
        if (!Confirm("Clear Identity Cache?\n\nThis will force fresh ESI lookups."))
            return;

        ClearIdentityCache();
        RefreshAll();
        RunSelectedQuery();
    }

    private void ClearActivity_Click(object sender, RoutedEventArgs e)
    {
        if (!Confirm("Clear Activity Cache?\n\nThis will clear derived activity rows, the stored Last Active, and the last recent-call time."))
            return;

        ClearActivityCache();
        RefreshAll();
        RunSelectedQuery();
    }

    private void ClearKillmail_Click(object sender, RoutedEventArgs e)
    {
        if (!Confirm("Clear Recent Killmail Cache?\n\nThis will force recent killmail retrieval and reset the last recent-call time. Retained qualifying killmails are not deleted."))
            return;

        ClearKillmailCache();
        RefreshAll();
        RunSelectedQuery();
    }

    private void ClearStatistics_Click(object sender, RoutedEventArgs e)
    {
        if (!Confirm("Clear Statistics Cache?\n\nThis will force fresh zKill statistics lookups."))
            return;

        ClearStatisticsCache();
        RefreshAll();
        RunSelectedQuery();
    }

    private void ClearAll_Click(object sender, RoutedEventArgs e)
    {
        if (!Confirm("Clear All Caches?\n\nThis will clear the identity, activity, recent killmail and statistics caches, and reset the last recent-call time. Retained qualifying killmails are not deleted."))
            return;

        ClearIdentityCache();
        ClearActivityCache();
        ClearKillmailCache();
        ClearStatisticsCache();
        RefreshAll();
        RunSelectedQuery();
    }

    private void ClearIdentityCache()
    {
        _service.ExecuteNonQuery("DELETE FROM main.pilot_identity_cache;");
    }

    private void ClearActivityCache()
    {
        _service.ExecuteNonQuery("DELETE FROM main.zkill_activity_cache;");
    }

    private void ClearKillmailCache()
    {
        _service.ExecuteNonQuery("""
            DELETE FROM main.zkill_killmail_attackers
            WHERE killmail_id IN (
                SELECT killmail_id
                FROM main.zkill_killmails
                WHERE is_qualifying = FALSE
            );
            """);
        _service.ExecuteNonQuery("DELETE FROM main.zkill_killmails WHERE is_qualifying = FALSE;");
        _service.ExecuteNonQuery("UPDATE main.zkill_activity_cache SET last_recent_call_utc = NULL;");
    }

    private void ClearStatisticsCache()
    {
        _service.ExecuteNonQuery("DELETE FROM main.zkill_statistics_cache;");
    }

    private void CopySummary_Click(object sender, RoutedEventArgs e)
    {
        Clipboard.SetText(DiagnosticsText.Text);
    }

    private static bool Confirm(string message)
    {
        return MessageBox.Show(
            message,
            "KillRight Developer Diagnostics",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning) == MessageBoxResult.Yes;
    }
}

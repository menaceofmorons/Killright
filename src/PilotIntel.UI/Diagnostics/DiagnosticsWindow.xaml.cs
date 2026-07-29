using System.Data;
using System.Text;
using System.Windows;
using PilotIntel.Shared.Time;

namespace PilotIntel.UI.Diagnostics;

public partial class DiagnosticsWindow : Window
{
    private static readonly Dictionary<string, string> DiagnosticQueries = new()
    {
        ["Pilot Refresh Status"] = """
            SELECT character_id,
                   MAX(kill_time_utc) AS latest_killmail_utc,
                   MAX(cached_at_utc) AS latest_cache_write_utc,
                   COUNT(*) AS cached_killmails
            FROM main.zkill_recent_killmail_cache
            GROUP BY character_id
            ORDER BY latest_killmail_utc DESC;
            """,

        ["Latest Killmail Per Pilot"] = """
            SELECT character_id,
                   MAX(kill_time_utc) AS latest_killmail_utc
            FROM main.zkill_recent_killmail_cache
            GROUP BY character_id
            ORDER BY latest_killmail_utc DESC;
            """,

        ["Killmail Count Per Pilot"] = """
            SELECT character_id,
                   COUNT(*) AS cached_killmails
            FROM main.zkill_recent_killmail_cache
            GROUP BY character_id
            ORDER BY character_id;
            """,

        ["Duplicate Killmail Check"] = """
            SELECT killmail_id,
                   COUNT(*) AS duplicate_count
            FROM main.zkill_recent_killmail_cache
            GROUP BY killmail_id
            HAVING COUNT(*) > 1;
            """,

        ["Expired Killmail Check"] = """
            SELECT *
            FROM main.zkill_recent_killmail_cache
            WHERE kill_time_utc < CAST((CURRENT_TIMESTAMP - INTERVAL '7 days') AS TEXT)
            ORDER BY kill_time_utc DESC;
            """,

        ["Recent Killmail Rows"] = """
            SELECT *
            FROM main.zkill_recent_killmail_cache
            ORDER BY kill_time_utc DESC
            LIMIT 100;
            """,

        ["Ship Usage Check"] = """
            SELECT killmail_id,
                   character_id,
                   is_loss,
                   ship_type_id,
                   attacker_count,
                   is_solo,
                   kill_time_utc
            FROM main.zkill_recent_killmail_cache
            WHERE ship_type_id IS NOT NULL
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
            """
    };

    private readonly DiagnosticsDataService _service;
    private DataTable _lastQueryRows = new();

    public DiagnosticsWindow()
    {
        InitializeComponent();
        _service = new DiagnosticsDataService(App.Database);

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
            $"Expired Killmail Rows:    {summary.ExpiredKillmailRows}";

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
            FROM main.zkill_recent_killmail_cache
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
               """;
    }

    private void RunSelectedQuery()
    {
        if (QuerySelector.SelectedItem is not string selected)
            return;

        if (!DiagnosticQueries.TryGetValue(selected, out var sql))
            return;

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

        _service.ExecuteNonQuery("DELETE FROM main.pilot_identity_cache;");
        RefreshAll();
        RunSelectedQuery();
    }

    private void ClearActivity_Click(object sender, RoutedEventArgs e)
    {
        if (!Confirm("Clear Activity Cache?\n\nThis will clear derived activity rows."))
            return;

        _service.ExecuteNonQuery("DELETE FROM main.zkill_activity_cache;");
        RefreshAll();
        RunSelectedQuery();
    }

    private void ClearKillmail_Click(object sender, RoutedEventArgs e)
    {
        if (!Confirm("Clear Recent Killmail Cache?\n\nThis will force recent killmail retrieval."))
            return;

        _service.ExecuteNonQuery("DELETE FROM main.zkill_recent_killmail_cache;");
        RefreshAll();
        RunSelectedQuery();
    }

    private void CopySummary_Click(object sender, RoutedEventArgs e)
    {
        Clipboard.SetText(DiagnosticsText.Text);
    }

    private static bool Confirm(string message)
    {
        return MessageBox.Show(
            message,
            "PilotIntel Developer Diagnostics",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning) == MessageBoxResult.Yes;
    }
}
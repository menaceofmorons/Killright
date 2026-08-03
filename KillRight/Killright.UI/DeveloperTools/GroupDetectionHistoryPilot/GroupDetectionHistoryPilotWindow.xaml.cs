using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Windows;
using Killright.Integration.zKill.History;
using Killright.Storage.GroupHistory;

namespace Killright.UI.DeveloperTools.GroupDetectionHistoryPilot;

public partial class GroupDetectionHistoryPilotWindow : Window
{
    private string? _lastResults;

    public GroupDetectionHistoryPilotWindow()
    {
        InitializeComponent();
        ImportDateTextBox.Text = DateOnly.FromDateTime(DateTime.UtcNow.Date.AddDays(-1)).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    private async void StartButton_Click(object sender, RoutedEventArgs e)
    {
        if (!DateOnly.TryParseExact(
                ImportDateTextBox.Text.Trim(),
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var importDateUtc))
        {
            MessageBox.Show(
                "Import date must use yyyy-MM-dd format.",
                "Invalid Date",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        StartButton.IsEnabled = false;
        CopyResultsButton.IsEnabled = false;
        _lastResults = null;
        ResultTextBox.Text = $"Importing one-day history counts for {importDateUtc:yyyy-MM-dd}...";

        var database = new DuckDbGroupHistoryDatabase();

        try
        {
            await database.EnsureCreatedAsync();
            await database.MarkImportDayStartedAsync(importDateUtc);

            using var handler = new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
            };

            using var httpClient = new HttpClient(handler);
            var client = new ZkillHistoryClient(httpClient);
            var result = await client.CountDayAsync(importDateUtc);

            if (!result.Succeeded)
            {
                await database.MarkImportDayFailedAsync(
                    importDateUtc,
                    result.ErrorMessage ?? "Unknown one-day history import failure.");

                _lastResults = BuildFailureReport(result);
                ResultTextBox.Text = _lastResults;
                CopyResultsButton.IsEnabled = true;
                return;
            }

            await database.MarkImportDayCompletedAsync(
                importDateUtc,
                result.RawKillmailCount,
                result.QualifyingKillmailCount,
                result.QualifyingAttackerCount,
                result.CandidatePairOccurrenceRows);

            _lastResults = BuildSuccessReport(result);
            ResultTextBox.Text = _lastResults;
            CopyResultsButton.IsEnabled = true;
        }
        catch (Exception ex)
        {
            await database.MarkImportDayFailedAsync(importDateUtc, ex.Message);
            _lastResults = $"One-day history import failed for {importDateUtc:yyyy-MM-dd}: {ex.Message}";
            ResultTextBox.Text = _lastResults;
            CopyResultsButton.IsEnabled = true;
        }
        finally
        {
            StartButton.IsEnabled = true;
        }
    }

    private static string BuildSuccessReport(ZkillHistoryDayResult result)
    {
        return
            "====================================================\r\n" +
            "KillRight Group Detection One-Day History Import\r\n" +
            "====================================================\r\n\r\n" +
            $"Date: {result.Date:yyyy-MM-dd}\r\n" +
            $"Source: {result.Url}\r\n" +
            "Status: Completed\r\n\r\n" +
            "Persisted to history_import_day_status only\r\n" +
            $"Raw killmails: {result.RawKillmailCount:N0}\r\n" +
            $"Raw attackers: {result.RawAttackerCount:N0}\r\n" +
            $"Pod killmails excluded: {result.PodKillmailCount:N0}\r\n" +
            $"Solo or insufficient attacker killmails excluded: {result.InsufficientAttackerKillmailCount:N0}\r\n" +
            $"Fleet killmails excluded: {result.FleetKillmailCount:N0}\r\n" +
            $"Qualifying killmails: {result.QualifyingKillmailCount:N0}\r\n" +
            $"Qualifying attackers / participant rows candidate count: {result.QualifyingAttackerCount:N0}\r\n" +
            $"Candidate pair occurrence rows: {result.CandidatePairOccurrenceRows:N0}\r\n" +
            $"Highest qualifying attackers on a single killmail: {result.MaxQualifyingAttackersOnKillmail:N0}\r\n\r\n" +
            "Notes:\r\n" +
            "- This step reuses the existing working zKill history count path.\r\n" +
            "- This step does not write historic_relationship_evidence rows.\r\n" +
            "- This step does not write historic_relationship_evidence_participants rows.\r\n" +
            "- This step does not write historic_relationship_summary rows.\r\n" +
            "====================================================";
    }

    private static string BuildFailureReport(ZkillHistoryDayResult result)
    {
        return
            "====================================================\r\n" +
            "KillRight Group Detection One-Day History Import\r\n" +
            "====================================================\r\n\r\n" +
            $"Date: {result.Date:yyyy-MM-dd}\r\n" +
            $"Source: {result.Url}\r\n" +
            "Status: Failed\r\n" +
            $"Error: {result.ErrorMessage}\r\n" +
            "====================================================";
    }

    private void CopyResultsButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_lastResults))
            return;

        Clipboard.SetText(_lastResults);
    }
}
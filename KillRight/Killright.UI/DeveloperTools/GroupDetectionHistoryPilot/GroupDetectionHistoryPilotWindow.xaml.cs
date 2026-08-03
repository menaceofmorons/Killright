using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Windows;
using Killright.Integration.zKill.History;
using Killright.Storage.GroupHistory;
using Killright.Storage.GroupHistory.Models;

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
        if (!DateOnly.TryParseExact(ImportDateTextBox.Text.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var importDateUtc))
        {
            MessageBox.Show("Import date must use yyyy-MM-dd format.", "Invalid Date", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        StartButton.IsEnabled = false;
        CopyResultsButton.IsEnabled = false;
        _lastResults = null;
        ResultTextBox.Text = $"Importing one-day evidence and participant rows for {importDateUtc:yyyy-MM-dd}...";

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
            var result = await client.ExtractDayEvidenceAsync(importDateUtc);

            if (!result.DayResult.Succeeded)
            {
                await database.MarkImportDayFailedAsync(
                    importDateUtc,
                    result.DayResult.ErrorMessage ?? "Unknown one-day participant import failure.");

                _lastResults = BuildFailureReport(result.DayResult);
                ResultTextBox.Text = _lastResults;
                CopyResultsButton.IsEnabled = true;
                return;
            }

            var evidenceRows = result.EvidenceRows
                .Select(row => new GroupHistoryEvidenceImportRow(
                    row.KillmailId,
                    row.KillmailTimeUtc,
                    row.EvidenceDateUtc,
                    row.SolarSystemId,
                    row.ParticipantCount))
                .ToArray();

            var participantRows = result.ParticipantRows
                .Select(row => new GroupHistoryParticipantImportRow(
                    row.EvidenceId,
                    row.CharacterId,
                    row.CorporationId,
                    row.AllianceId,
                    row.ShipTypeId))
                .ToArray();

            await database.ImportEvidenceAndParticipantRowsForDayAsync(
                importDateUtc,
                evidenceRows,
                participantRows,
                result.DayResult.RawKillmailCount,
                result.DayResult.QualifyingKillmailCount,
                result.DayResult.QualifyingAttackerCount,
                result.DayResult.CandidatePairOccurrenceRows);

            _lastResults = BuildSuccessReport(result.DayResult, evidenceRows.Length, participantRows.Length);
            ResultTextBox.Text = _lastResults;
            CopyResultsButton.IsEnabled = true;
        }
        catch (Exception ex)
        {
            await database.MarkImportDayFailedAsync(importDateUtc, ex.Message);
            _lastResults = $"One-day participant import failed for {importDateUtc:yyyy-MM-dd}: {ex.Message}";
            ResultTextBox.Text = _lastResults;
            CopyResultsButton.IsEnabled = true;
        }
        finally
        {
            StartButton.IsEnabled = true;
        }
    }

    private static string BuildSuccessReport(ZkillHistoryDayResult result, int persistedEvidenceRows, int persistedParticipantRows)
    {
        return
            "====================================================\r\n" +
            "KillRight Group Detection One-Day Participant Import\r\n" +
            "====================================================\r\n\r\n" +
            $"Date: {result.Date:yyyy-MM-dd}\r\n" +
            $"Source: {result.Url}\r\n" +
            "Status: Completed\r\n\r\n" +
            $"Raw killmails: {result.RawKillmailCount:N0}\r\n" +
            $"Raw attackers: {result.RawAttackerCount:N0}\r\n" +
            $"Pod killmails excluded: {result.PodKillmailCount:N0}\r\n" +
            $"Solo or insufficient attacker killmails excluded: {result.InsufficientAttackerKillmailCount:N0}\r\n" +
            $"Fleet killmails excluded: {result.FleetKillmailCount:N0}\r\n" +
            $"Qualifying killmails: {result.QualifyingKillmailCount:N0}\r\n" +
            $"Persisted evidence rows: {persistedEvidenceRows:N0}\r\n" +
            $"Qualifying attackers: {result.QualifyingAttackerCount:N0}\r\n" +
            $"Persisted participant rows: {persistedParticipantRows:N0}\r\n" +
            $"Candidate pair occurrence rows: {result.CandidatePairOccurrenceRows:N0}\r\n" +
            $"Highest qualifying attackers on a single killmail: {result.MaxQualifyingAttackersOnKillmail:N0}\r\n\r\n" +
            "Notes:\r\n" +
            "- This step writes historic_relationship_evidence rows.\r\n" +
            "- This step writes historic_relationship_evidence_participants rows.\r\n" +
            "- This step does not write historic_relationship_summary rows.\r\n" +
            "- Persisted evidence rows should equal qualifying killmails.\r\n" +
            "- Persisted participant rows should equal qualifying attackers.\r\n" +
            "====================================================";
    }

    private static string BuildFailureReport(ZkillHistoryDayResult result)
    {
        return
            "====================================================\r\n" +
            "KillRight Group Detection One-Day Participant Import\r\n" +
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
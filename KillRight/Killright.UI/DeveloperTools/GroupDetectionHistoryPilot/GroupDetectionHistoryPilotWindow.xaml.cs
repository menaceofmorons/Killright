using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text;
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
        SetDefaultDateRange();
    }

    private void SetDefaultDateRange()
    {
        var yesterday = DateOnly.FromDateTime(DateTime.UtcNow.Date.AddDays(-1));
        StartDateTextBox.Text = yesterday.AddDays(-6).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        EndDateTextBox.Text = yesterday.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    private async void StartButton_Click(object sender, RoutedEventArgs e)
    {
        if (!TryReadDateRange(out var startDateUtc, out var endDateUtc))
            return;

        StartButton.IsEnabled = false;
        CopyResultsButton.IsEnabled = false;
        _lastResults = null;
        ResultTextBox.Text = "Starting multi-day import...";
        StatusTextBlock.Text = "Running...";

        try
        {
            var database = new DuckDbGroupHistoryDatabase();
            await database.EnsureCreatedAsync();

            using var handler = new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
            };

            using var httpClient = new HttpClient(handler);
            var client = new ZkillHistoryClient(httpClient);
            var summary = await ImportDateRangeAsync(database, client, startDateUtc, endDateUtc);

            _lastResults = BuildSummaryReport(summary);
            ResultTextBox.Text = _lastResults;
            StatusTextBlock.Text = "Completed.";
            CopyResultsButton.IsEnabled = true;
        }
        catch (Exception ex)
        {
            _lastResults = $"Multi-day import failed: {ex.Message}";
            ResultTextBox.Text = _lastResults;
            StatusTextBlock.Text = "Failed.";
            CopyResultsButton.IsEnabled = true;
        }
        finally
        {
            StartButton.IsEnabled = true;
        }
    }

    private async Task<GroupHistoryMultiDayImportSummary> ImportDateRangeAsync(
        DuckDbGroupHistoryDatabase database,
        ZkillHistoryClient client,
        DateOnly startDateUtc,
        DateOnly endDateUtc)
    {
        var runStopwatch = Stopwatch.StartNew();
        var importedDayElapsed = TimeSpan.Zero;
        var dates = GetDateRange(startDateUtc, endDateUtc);
        var logLines = new List<string>();
        var importedDays = 0;
        var skippedDays = 0;
        var failedDays = 0;
        var rawKillmailCount = 0;
        var qualifyingKillmailCount = 0;
        var evidenceRowCount = 0;
        var participantRowCount = 0;
        var candidatePairOccurrenceRows = 0L;
        var summaryPairOccurrenceRows = 0L;
        var totalSummaryRows = 0L;

        for (var index = 0; index < dates.Count; index++)
        {
            var importDateUtc = dates[index];
            var dayStopwatch = Stopwatch.StartNew();
            var dayStartedUtc = DateTime.UtcNow;
            StatusTextBlock.Text = $"Processing {importDateUtc:yyyy-MM-dd} ({index + 1}/{dates.Count})...";

            if (await database.IsImportDayCompletedAsync(importDateUtc))
            {
                skippedDays++;
                logLines.Add($"SKIPPED {importDateUtc:yyyy-MM-dd} already completed at {DateTime.UtcNow:O}.");
                ResultTextBox.Text = BuildLiveReport(startDateUtc, endDateUtc, dates.Count, importedDays, skippedDays, failedDays,
                    rawKillmailCount, qualifyingKillmailCount, evidenceRowCount, participantRowCount,
                    candidatePairOccurrenceRows, summaryPairOccurrenceRows, totalSummaryRows, runStopwatch.Elapsed,
                    importedDays == 0 ? TimeSpan.Zero : TimeSpan.FromTicks(importedDayElapsed.Ticks / importedDays), logLines);
                continue;
            }

            await database.MarkImportDayStartedAsync(importDateUtc);

            try
            {
                var result = await client.ExtractDayEvidenceAsync(importDateUtc);

                if (!result.DayResult.Succeeded)
                {
                    failedDays++;
                    await database.MarkImportDayFailedAsync(importDateUtc, result.DayResult.ErrorMessage ?? "Unknown multi-day import failure.");
                    logLines.Add($"FAILED {importDateUtc:yyyy-MM-dd} started={dayStartedUtc:O} completed={DateTime.UtcNow:O} error={result.DayResult.ErrorMessage}");
                    continue;
                }

                var conversionStopwatch = Stopwatch.StartNew();

                var evidenceRows = result.EvidenceRows
                    .Select(row => new GroupHistoryEvidenceImportRow(row.KillmailId, row.KillmailTimeUtc, row.EvidenceDateUtc, row.SolarSystemId, row.ParticipantCount))
                    .ToArray();

                var participantRows = result.ParticipantRows
                    .Select(row => new GroupHistoryParticipantImportRow(row.EvidenceId, row.CharacterId, row.CorporationId, row.AllianceId, row.ShipTypeId))
                    .ToArray();

                conversionStopwatch.Stop();

                var summaryResult = await database.ImportEvidenceAndParticipantRowsAndUpdateSummaryForDayAsync(
                    importDateUtc,
                    evidenceRows,
                    participantRows,
                    result.DayResult.RawKillmailCount,
                    result.DayResult.QualifyingKillmailCount,
                    result.DayResult.QualifyingAttackerCount,
                    result.DayResult.CandidatePairOccurrenceRows);

                dayStopwatch.Stop();
                importedDayElapsed += dayStopwatch.Elapsed;
                importedDays++;
                rawKillmailCount += result.DayResult.RawKillmailCount;
                qualifyingKillmailCount += result.DayResult.QualifyingKillmailCount;
                evidenceRowCount += evidenceRows.Length;
                participantRowCount += participantRows.Length;
                candidatePairOccurrenceRows += result.DayResult.CandidatePairOccurrenceRows;
                summaryPairOccurrenceRows += summaryResult.PairOccurrenceRows;
                totalSummaryRows = summaryResult.TotalSummaryRows;

                logLines.Add(BuildTimingLine(importDateUtc, dayStartedUtc, DateTime.UtcNow, dayStopwatch.Elapsed, result, conversionStopwatch.Elapsed, summaryResult));
            }
            catch (Exception ex)
            {
                dayStopwatch.Stop();
                failedDays++;
                await database.MarkImportDayFailedAsync(importDateUtc, ex.Message);
                logLines.Add($"FAILED {importDateUtc:yyyy-MM-dd} started={dayStartedUtc:O} completed={DateTime.UtcNow:O} elapsed={FormatElapsed(dayStopwatch.Elapsed)} error={ex.Message}");
            }

            ResultTextBox.Text = BuildLiveReport(startDateUtc, endDateUtc, dates.Count, importedDays, skippedDays, failedDays,
                rawKillmailCount, qualifyingKillmailCount, evidenceRowCount, participantRowCount,
                candidatePairOccurrenceRows, summaryPairOccurrenceRows, totalSummaryRows, runStopwatch.Elapsed,
                importedDays == 0 ? TimeSpan.Zero : TimeSpan.FromTicks(importedDayElapsed.Ticks / importedDays), logLines);
        }

        runStopwatch.Stop();

        return new GroupHistoryMultiDayImportSummary(
            startDateUtc,
            endDateUtc,
            dates.Count,
            importedDays,
            skippedDays,
            failedDays,
            rawKillmailCount,
            qualifyingKillmailCount,
            evidenceRowCount,
            participantRowCount,
            candidatePairOccurrenceRows,
            summaryPairOccurrenceRows,
            totalSummaryRows,
            runStopwatch.Elapsed,
            importedDays == 0 ? TimeSpan.Zero : TimeSpan.FromTicks(importedDayElapsed.Ticks / importedDays),
            logLines);
    }

    private static string BuildTimingLine(
        DateOnly importDateUtc,
        DateTime startedUtc,
        DateTime completedUtc,
        TimeSpan dayElapsed,
        ZkillHistoryEvidenceDayResult result,
        TimeSpan conversionElapsed,
        GroupHistorySummaryBuildResult summaryResult)
    {
        var extraction = result.Timing;
        var persistence = summaryResult.Timing;
        var measuredDay =
            extraction.DownloadElapsed +
            extraction.JsonParseElapsed +
            extraction.RowGenerationElapsed +
            conversionElapsed +
            persistence.TotalPersistenceElapsed;

        var unaccountedDay = dayElapsed - measuredDay;

        if (unaccountedDay < TimeSpan.Zero)
            unaccountedDay = TimeSpan.Zero;

        return
            $"IMPORTED {importDateUtc:yyyy-MM-dd} " +
            $"started={startedUtc:O} completed={completedUtc:O} total={FormatElapsed(dayElapsed)} " +
            $"download={FormatElapsed(extraction.DownloadElapsed)} parse={FormatElapsed(extraction.JsonParseElapsed)} rowBuild={FormatElapsed(extraction.RowGenerationElapsed)} uiConvert={FormatElapsed(conversionElapsed)} " +
            $"connectionOpen={FormatElapsed(persistence.ConnectionOpenElapsed)} transactionBegin={FormatElapsed(persistence.TransactionBeginElapsed)} " +
            $"evidencePrep={FormatElapsed(persistence.EvidenceBatchPreparationElapsed)} evidenceExec={FormatElapsed(persistence.EvidenceBatchExecutionElapsed)} evidenceInsert={FormatElapsed(persistence.EvidenceInsertElapsed)} " +
            $"participantPrep={FormatElapsed(persistence.ParticipantBatchPreparationElapsed)} participantExec={FormatElapsed(persistence.ParticipantBatchExecutionElapsed)} participantInsert={FormatElapsed(persistence.ParticipantInsertElapsed)} " +
            $"summaryPreCount={FormatElapsed(persistence.SummaryPreCountElapsed)} summaryUpdate={FormatElapsed(persistence.SummaryUpdateElapsed)} summaryPostCount={FormatElapsed(persistence.SummaryPostCountElapsed)} " +
            $"statusUpdate={FormatElapsed(persistence.StatusUpdateElapsed)} commit={FormatElapsed(persistence.TransactionCommitElapsed)} dbTotal={FormatElapsed(persistence.TotalPersistenceElapsed)} " +
            $"dbUnaccounted={FormatElapsed(persistence.UnaccountedPersistenceElapsed)} dayUnaccounted={FormatElapsed(unaccountedDay)} " +
            $"raw={result.DayResult.RawKillmailCount:N0} qualifying={result.DayResult.QualifyingKillmailCount:N0} evidence={result.EvidenceRows.Count:N0} participants={result.ParticipantRows.Count:N0} pairs={summaryResult.PairOccurrenceRows:N0} totalSummary={summaryResult.TotalSummaryRows:N0}";
    }

    private bool TryReadDateRange(out DateOnly startDateUtc, out DateOnly endDateUtc)
    {
        startDateUtc = default;
        endDateUtc = default;

        if (!DateOnly.TryParseExact(StartDateTextBox.Text.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out startDateUtc))
        {
            MessageBox.Show("Start date must use yyyy-MM-dd format.", "Invalid Date", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        if (!DateOnly.TryParseExact(EndDateTextBox.Text.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out endDateUtc))
        {
            MessageBox.Show("End date must use yyyy-MM-dd format.", "Invalid Date", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        if (endDateUtc < startDateUtc)
        {
            MessageBox.Show("End date must be on or after start date.", "Invalid Date Range", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        var latestImportableDay = DateOnly.FromDateTime(DateTime.UtcNow.Date.AddDays(-1));

        if (endDateUtc > latestImportableDay)
        {
            MessageBox.Show("End date must not be later than yesterday UTC.", "Invalid Date Range", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        return true;
    }

    private static List<DateOnly> GetDateRange(DateOnly startDateUtc, DateOnly endDateUtc)
    {
        var dates = new List<DateOnly>();

        for (var date = startDateUtc; date <= endDateUtc; date = date.AddDays(1))
            dates.Add(date);

        return dates;
    }

    private static string BuildLiveReport(DateOnly startDateUtc, DateOnly endDateUtc, int totalDays, int importedDays, int skippedDays, int failedDays, int rawKillmailCount, int qualifyingKillmailCount, int evidenceRowCount, int participantRowCount, long candidatePairOccurrenceRows, long summaryPairOccurrenceRows, long totalSummaryRows, TimeSpan totalRunElapsed, TimeSpan averageImportedDayElapsed, IReadOnlyList<string> logLines)
    {
        return BuildReportText("KillRight Group Detection Multi-Day Import Running", startDateUtc, endDateUtc, totalDays, importedDays, skippedDays, failedDays, rawKillmailCount, qualifyingKillmailCount, evidenceRowCount, participantRowCount, candidatePairOccurrenceRows, summaryPairOccurrenceRows, totalSummaryRows, totalRunElapsed, averageImportedDayElapsed, logLines);
    }

    private static string BuildSummaryReport(GroupHistoryMultiDayImportSummary summary)
    {
        return BuildReportText("KillRight Group Detection Multi-Day Import Completed", summary.StartDateUtc, summary.EndDateUtc, summary.TotalDays, summary.ImportedDays, summary.SkippedDays, summary.FailedDays, summary.RawKillmailCount, summary.QualifyingKillmailCount, summary.EvidenceRows, summary.ParticipantRows, summary.CandidatePairOccurrenceRows, summary.SummaryPairOccurrenceRows, summary.TotalSummaryRows, summary.TotalRunElapsed, summary.AverageImportedDayElapsed, summary.LogLines);
    }

    private static string BuildReportText(string title, DateOnly startDateUtc, DateOnly endDateUtc, int totalDays, int importedDays, int skippedDays, int failedDays, int rawKillmailCount, int qualifyingKillmailCount, int evidenceRowCount, int participantRowCount, long candidatePairOccurrenceRows, long summaryPairOccurrenceRows, long totalSummaryRows, TimeSpan totalRunElapsed, TimeSpan averageImportedDayElapsed, IReadOnlyList<string> logLines)
    {
        var builder = new StringBuilder();
        builder.AppendLine("====================================================");
        builder.AppendLine(title);
        builder.AppendLine("====================================================");
        builder.AppendLine();
        builder.AppendLine($"Range: {startDateUtc:yyyy-MM-dd} to {endDateUtc:yyyy-MM-dd}");
        builder.AppendLine($"Total days: {totalDays:N0}");
        builder.AppendLine($"Imported days: {importedDays:N0}");
        builder.AppendLine($"Skipped days: {skippedDays:N0}");
        builder.AppendLine($"Failed days: {failedDays:N0}");
        builder.AppendLine($"Total run elapsed: {FormatElapsed(totalRunElapsed)}");
        builder.AppendLine($"Average imported day elapsed: {FormatElapsed(averageImportedDayElapsed)}");
        builder.AppendLine();
        builder.AppendLine($"Raw killmails: {rawKillmailCount:N0}");
        builder.AppendLine($"Qualifying killmails: {qualifyingKillmailCount:N0}");
        builder.AppendLine($"Persisted evidence rows: {evidenceRowCount:N0}");
        builder.AppendLine($"Persisted participant rows: {participantRowCount:N0}");
        builder.AppendLine($"Candidate pair occurrence rows: {candidatePairOccurrenceRows:N0}");
        builder.AppendLine($"Summary pair occurrence rows added: {summaryPairOccurrenceRows:N0}");
        builder.AppendLine($"Total unique summary rows: {totalSummaryRows:N0}");
        builder.AppendLine();
        builder.AppendLine("Notes:");
        builder.AppendLine("- Days are imported sequentially.");
        builder.AppendLine("- Completed days are skipped on rerun.");
        builder.AppendLine("- Relationship summaries are updated incrementally.");
        builder.AppendLine("- Deep timing lines report connection, transaction, batch preparation, batch execution, summary count and unaccounted phases.");
        builder.AppendLine();
        builder.AppendLine("Log:");

        foreach (var line in logLines)
            builder.AppendLine(line);

        builder.AppendLine("====================================================");
        return builder.ToString();
    }

    private static string FormatElapsed(TimeSpan value)
    {
        return $"{value.TotalSeconds:N3}s";
    }

    private void CopyResultsButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_lastResults))
            return;

        Clipboard.SetText(_lastResults);
    }
}
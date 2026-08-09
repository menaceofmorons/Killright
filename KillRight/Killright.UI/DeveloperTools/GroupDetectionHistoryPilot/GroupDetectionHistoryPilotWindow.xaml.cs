using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using Killright.Storage.GroupHistory;
using Killright.Storage.GroupHistory.Models;

namespace Killright.UI.DeveloperTools.GroupDetectionHistoryPilot;

public partial class GroupDetectionHistoryPilotWindow : Window
{
    private static readonly DateOnly TestModeAnchorStartDate = new(2016, 8, 1);
    private static readonly List<string> _runHistoryLines = new();

    private readonly DispatcherTimer _statusTimer;
    private Stopwatch? _localRunStopwatch;
    private Process? _launchedProcess;
    private bool _lastObservedUpdateInProgress;
    private string? _pendingRunDescription;
    private string? _baselineLastUpdatedUtc;
    private string? _baselineLastCompletedDayUtc;

    public GroupDetectionHistoryPilotWindow()
    {
        InitializeComponent();

        _statusTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _statusTimer.Tick += StatusTimer_Tick;

        Loaded += GroupDetectionHistoryPilotWindow_Loaded;
        Closed += GroupDetectionHistoryPilotWindow_Closed;
    }

    private void GroupDetectionHistoryPilotWindow_Loaded(object sender, RoutedEventArgs e)
    {
        RefreshStatus();
        _statusTimer.Start();
    }

    private void GroupDetectionHistoryPilotWindow_Closed(object? sender, EventArgs e)
    {
        _statusTimer.Stop();
    }

    private void LaunchButton_Click(object sender, RoutedEventArgs e)
    {
        var executablePath = HistoryUpdaterExecutableLocator.Locate();

        if (executablePath is null)
        {
            MessageBox.Show(
                $"Could not locate {HistoryUpdaterExecutableLocator.ExecutableFileName}. Build the killright_history_updater crate (cargo build or cargo build --release) and try again.",
                "Historic Updater Not Found",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        var arguments = "build-staging";
        var description = "Default (10 years back from today)";
        var amountText = TestAmountTextBox.Text.Trim();

        if (amountText.Length > 0)
        {
            if (!int.TryParse(amountText, out var amount) || amount < 1)
            {
                MessageTextBlock.Text = "Test Amount must be a whole number of at least 1, or left blank for the full default (10 years back from today).";
                return;
            }

            var unit = DaysRadioButton.IsChecked == true ? "days"
                : MonthsRadioButton.IsChecked == true ? "months"
                : "years";

            arguments = $"build-staging --test-amount {amount} --test-amount-unit {unit}";
            description = $"{amount} {CapitalizeFirst(unit)} from 01 Aug 2016";
        }

        try
        {
            var preLaunchStatus = GroupHistoryLiveStatusLoader.LoadOrDefault();
            _baselineLastUpdatedUtc = preLaunchStatus.LastUpdatedUtc;
            _baselineLastCompletedDayUtc = preLaunchStatus.LastCompletedDayUtc;
            _launchedProcess = HistoryUpdaterProcessLauncher.LaunchDetached(executablePath, arguments);
            _localRunStopwatch = Stopwatch.StartNew();
            _pendingRunDescription = description;
            MessageTextBlock.Text = $"Launched {executablePath} {arguments}.";
        }
        catch (Exception ex)
        {
            _launchedProcess = null;
            _pendingRunDescription = null;
            _baselineLastUpdatedUtc = null;
            _baselineLastCompletedDayUtc = null;
            MessageTextBlock.Text = $"Failed to launch the historic updater: {ex.Message}";
        }

        RefreshStatus();
    }

    private void StopButton_Click(object sender, RoutedEventArgs e)
    {
        if (_launchedProcess is null || _launchedProcess.HasExited)
            return;

        try
        {
            _launchedProcess.Kill();
            _launchedProcess.WaitForExit(5000);

            GroupHistoryLiveStatusResetter.ResetUpdateInProgress();
            var deletedOrphan = HistoryUpdaterOrphanedStagingFileCleaner.TryCleanUpOrphan();
            var progressLogPath = deletedOrphan is null ? null : Path.ChangeExtension(deletedOrphan, ".progress.log");
            var progressLogFileName = progressLogPath is not null && File.Exists(progressLogPath)
                ? Path.GetFileName(progressLogPath)
                : null;

            var status = GroupHistoryLiveStatusLoader.LoadOrDefault();
            RecordRunHistoryEntry("Stopped by user", status.LastCompletedDayUtc, progressLogFileName);

            MessageTextBlock.Text = deletedOrphan is null
                ? "Stopped the historic updater."
                : $"Stopped the historic updater and removed the abandoned staging file {Path.GetFileName(deletedOrphan)}.";
        }
        catch (Exception ex)
        {
            MessageTextBlock.Text = $"Failed to stop the historic updater: {ex.Message}";
        }
        finally
        {
            _localRunStopwatch = null;
            _launchedProcess = null;
            _pendingRunDescription = null;
            _baselineLastUpdatedUtc = null;
            _baselineLastCompletedDayUtc = null;
        }

        RefreshStatus();
    }

    private void WipeButton_Click(object sender, RoutedEventArgs e)
    {
        var currentStatus = GroupHistoryLiveStatusLoader.LoadOrDefault();

        if (currentStatus.UpdateInProgress)
        {
            MessageBox.Show(
                "A historic update is currently in progress. Stop it (if this window launched it) or wait for it to finish before wiping.",
                "Historic Updater Running",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        var confirmed = MessageBox.Show(
            "This permanently deletes the active historic database, every staging file, and the live status file, so the next launch starts completely from zero. This cannot be undone. Continue?",
            "Wipe and Start Anew",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirmed != MessageBoxResult.Yes)
            return;

        try
        {
            var deletedCount = HistoryUpdaterWiper.WipeAll();
            MessageTextBlock.Text = $"Wiped {deletedCount} file(s). Ready to start anew.";
        }
        catch (Exception ex)
        {
            MessageTextBlock.Text = $"Failed to wipe: {ex.Message}";
        }

        RefreshStatus();
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        RefreshStatus();
    }

    private void CopyResultsButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(StatusTextBox.Text))
            return;

        Clipboard.SetText(StatusTextBox.Text);
    }

    private void StatusTimer_Tick(object? sender, EventArgs e)
    {
        RefreshStatus();
    }

    private void RefreshStatus()
    {
        // Step CC-08.08.26.01: self-heals a stuck updateInProgress flag left
        // by a process that is no longer running (crash, window closed
        // mid-run, or a killed background/test process) -- a no-op
        // otherwise. This is the only path that can recover Launch when the
        // current window instance never launched the run that set the flag.
        GroupHistoryLiveStatusResetter.ResetIfStale();

        var status = GroupHistoryLiveStatusLoader.LoadOrDefault();

        if (_lastObservedUpdateInProgress && !status.UpdateInProgress)
        {
            var outcome = status.LastUpdatedUtc != _baselineLastUpdatedUtc
                ? "Completed"
                : "Validation failed (no new data persisted)";
            RecordRunHistoryEntry(outcome, status.LastCompletedDayUtc);
            _localRunStopwatch = null;
        }

        _lastObservedUpdateInProgress = status.UpdateInProgress;

        LaunchButton.IsEnabled = !status.UpdateInProgress;
        StopButton.IsEnabled = _launchedProcess is not null && !_launchedProcess.HasExited;
        StatusTextBox.Text = BuildStatusReport(status);
    }

    private void RecordRunHistoryEntry(string outcome, string? currentLastCompletedDayUtc, string? progressLogFileName = null)
    {
        if (_pendingRunDescription is null)
            return;

        var elapsed = _localRunStopwatch?.Elapsed ?? TimeSpan.Zero;
        var timestamp = DateTime.Now.ToString("HH:mm:ss");
        var elapsedText = $"{(int)elapsed.TotalHours:D2}:{elapsed.Minutes:D2}:{elapsed.Seconds:D2}";
        var rangeText = DescribeImportedRange(_baselineLastCompletedDayUtc, currentLastCompletedDayUtc);

        // Only a Halted run (StopButton_Click) ever passes progressLogFileName --
        // the automatic completion/validation-failed detection in RefreshStatus
        // never does, so a normal run's line never carries this suffix.
        var progressLogText = progressLogFileName is null ? string.Empty : $" - Progress log: {progressLogFileName}";

        _runHistoryLines.Add($"[{timestamp}] {_pendingRunDescription} ({rangeText}) - {outcome} - Elapsed {elapsedText}{progressLogText}");

        _pendingRunDescription = null;
        _baselineLastUpdatedUtc = null;
        _baselineLastCompletedDayUtc = null;
    }

    /// <summary>
    /// Describes the actual date range a run added, based on what was already Completed
    /// (LastCompletedDayUtc) immediately before it launched versus after it finished or was
    /// stopped -- not the requested amount, which under the additive design (Section 1.0)
    /// rarely starts at the fixed anchor except for the very first run.
    /// </summary>
    private static string DescribeImportedRange(string? baselineLastCompletedDayUtc, string? currentLastCompletedDayUtc)
    {
        if (string.IsNullOrEmpty(currentLastCompletedDayUtc))
            return "no days completed yet";

        if (string.Equals(currentLastCompletedDayUtc, baselineLastCompletedDayUtc, StringComparison.Ordinal))
            return "no new days completed this run";

        var rangeStart = string.IsNullOrEmpty(baselineLastCompletedDayUtc)
            ? TestModeAnchorStartDate
            : DateOnly.ParseExact(baselineLastCompletedDayUtc, "yyyy-MM-dd", CultureInfo.InvariantCulture).AddDays(1);

        var rangeStartText = rangeStart.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        return rangeStartText == currentLastCompletedDayUtc
            ? rangeStartText
            : $"{rangeStartText} to {currentLastCompletedDayUtc}";
    }

    private static string CapitalizeFirst(string value) =>
        value.Length == 0 ? value : char.ToUpperInvariant(value[0]) + value[1..];

    private string BuildStatusReport(GroupHistoryLiveStatus status)
    {
        var builder = new StringBuilder();
        builder.AppendLine("====================================================");
        builder.AppendLine("KillRight Historic Updater - Live Status");
        builder.AppendLine("====================================================");
        builder.AppendLine();
        builder.AppendLine($"Update in progress: {(status.UpdateInProgress ? "Yes" : "No")}");
        builder.AppendLine($"Elapsed (this window): {FormatElapsed()}");
        builder.AppendLine($"Active database file: {(string.IsNullOrEmpty(status.ActiveDatabaseFile) ? "(none yet)" : status.ActiveDatabaseFile)}");
        builder.AppendLine($"Schema version: {status.SchemaVersion}");
        builder.AppendLine($"Last completed day (UTC): {status.LastCompletedDayUtc ?? "(none yet)"}");
        builder.AppendLine($"Last updated (UTC): {status.LastUpdatedUtc ?? "(none yet)"}");
        builder.AppendLine();
        builder.AppendLine("Notes:");
        builder.AppendLine("- This window only reads groupHistory.status.json; it performs no import or persistence itself.");
        builder.AppendLine("- The elapsed clock and Stop only track a run launched from this window during this session.");
        builder.AppendLine("- Test Amount is anchored at a fixed 01 Aug 2016 start date; it does not depend on today's date.");
        builder.AppendLine("====================================================");
        builder.AppendLine();
        builder.AppendLine("Run History (this window session):");

        if (_runHistoryLines.Count == 0)
        {
            builder.AppendLine("(no runs completed or stopped yet)");
        }
        else
        {
            foreach (var line in _runHistoryLines)
                builder.AppendLine(line);
        }

        builder.AppendLine("====================================================");
        return builder.ToString();
    }

    private string FormatElapsed()
    {
        if (_localRunStopwatch is null)
            return _lastObservedUpdateInProgress ? "(unknown - started outside this window session)" : "(not running)";

        var elapsed = _localRunStopwatch.Elapsed;
        return $"{(int)elapsed.TotalHours:D2}:{elapsed.Minutes:D2}:{elapsed.Seconds:D2}";
    }
}

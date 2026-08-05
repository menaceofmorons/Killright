using System.Diagnostics;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using Killright.Storage.GroupHistory;
using Killright.Storage.GroupHistory.Models;

namespace Killright.UI.DeveloperTools.GroupDetectionHistoryPilot;

public partial class GroupDetectionHistoryPilotWindow : Window
{
    private readonly DispatcherTimer _statusTimer;
    private Stopwatch? _localRunStopwatch;
    private bool _lastObservedUpdateInProgress;

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

        try
        {
            HistoryUpdaterProcessLauncher.LaunchDetached(executablePath, "build-staging");
            _localRunStopwatch = Stopwatch.StartNew();
            MessageTextBlock.Text = $"Launched {executablePath} build-staging.";
        }
        catch (Exception ex)
        {
            MessageTextBlock.Text = $"Failed to launch the historic updater: {ex.Message}";
        }

        RefreshStatus();
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        RefreshStatus();
    }

    private void StatusTimer_Tick(object? sender, EventArgs e)
    {
        RefreshStatus();
    }

    private void RefreshStatus()
    {
        var status = GroupHistoryLiveStatusLoader.LoadOrDefault();

        if (_lastObservedUpdateInProgress && !status.UpdateInProgress)
            _localRunStopwatch = null;

        _lastObservedUpdateInProgress = status.UpdateInProgress;

        LaunchButton.IsEnabled = !status.UpdateInProgress;
        StatusTextBox.Text = BuildStatusReport(status);
    }

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
        builder.AppendLine("- The elapsed clock only tracks a run launched from this window during this session; it shows as unknown if a run was already in progress when this window opened.");
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

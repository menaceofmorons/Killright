using System.Windows;
using System.Windows.Interop;
using Killright.Core.Activity;
using Killright.Core.Style;
using Killright.Integration.zKill;
using Killright.Shared;
using Killright.Shared.Constants;
using Killright.Shared.Time;
using Killright.Shared.zKill;
using Killright.UI.ClipboardMonitoring;
#if HISTORIC_RELATIONSHIPS
using Killright.UI.DeveloperTools.GroupDetectionHistoryPilot;
#endif
using Killright.UI.Diagnostics;
using Killright.UI.ViewModels;

namespace Killright.UI;

public partial class MainWindow : Window
{
    private readonly MainWindowViewModel _viewModel;
    private ClipboardMonitor? _clipboardMonitor;

    public MainWindow()
    {
        InitializeComponent();
#if HISTORIC_RELATIONSHIPS
        var historyPilotItem = new System.Windows.Controls.MenuItem
        {
            Header = "Group Detection History Pilot"
        };
        historyPilotItem.Click += GroupDetectionHistoryPilot_Click;
        DeveloperMenu.Items.Insert(1, new System.Windows.Controls.Separator());
        DeveloperMenu.Items.Insert(2, historyPilotItem);
#endif
        _viewModel = new MainWindowViewModel();
        DataContext = _viewModel;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _clipboardMonitor = new ClipboardMonitor(new WindowInteropHelper(this).Handle);
        _clipboardMonitor.ClipboardChanged += ClipboardChanged;
    }

    private async void ClipboardChanged(object? sender, EventArgs e)
    {
        string text;

        try
        {
            if (!System.Windows.Clipboard.ContainsText())
                return;

            text = System.Windows.Clipboard.GetText();
        }
        catch
        {
            // Clipboard access can transiently fail (COMException,
            // CLIPBRD_E_CANT_OPEN) whenever another process briefly holds
            // the clipboard open -- not worth crashing the app over. The
            // next clipboard change will try again (Step CC-12.08.26.02).
            return;
        }

        var pilotNames = PilotListParser.ParseIfLikelyPilotList(text);

        if (pilotNames.Count == 0)
            return;

        await ResolvePilotsAsync(pilotNames);
    }

    private async Task ResolvePilotsAsync(IReadOnlyList<string> pilotNames)
    {
        var rows = new List<PilotReportRow>();

        foreach (var pilotName in pilotNames)
        {
            var pilot = await App.PilotIdentityCache.GetAsync(pilotName, CacheDurations.PilotIdentity);

            if (pilot is null)
            {
                pilot = await App.EsiClient.ResolvePilotAsync(pilotName);

                if (pilot is null)
                    continue;

                await App.PilotIdentityCache.UpsertAsync(pilot);
            }

            if (pilot.VerifyStatus is VerifyStatus.NoMatch or VerifyStatus.Failed)
                continue;

            zKillActivity? activity = null;
            zKillStatistics? statistics = null;
            var recentStyle = StyleClassification.Unknown;
            var threatBand = "Unk";
            var statisticsCallFailed = false;
            var recentCallFailed = false;
            string? engineFailureReason = null;

            if (pilot.CharacterId is not null)
            {
                var characterId = pilot.CharacterId.Value;

                var (loadedStatistics, statisticsFetchedThisScan) = await LoadzKillStatisticsAsync(characterId);
                statistics = loadedStatistics;
                statisticsCallFailed = statisticsFetchedThisScan && statistics is null;

                var storedActivity = await SafeGetStoredActivityAsync(characterId);

                var (newLastSuccessfulCallUtc, recentCallDidFail) = await RefreshRecentKillmailsAsync(
                    characterId,
                    storedActivity,
                    statistics,
                    statisticsFetchedThisScan);
                recentCallFailed = recentCallDidFail;

                var killmailDerivedActivity = await LoadDerivedActivityAsync(characterId);

                activity = await ResolveActivityAsync(
                    characterId,
                    statistics,
                    storedActivity,
                    killmailDerivedActivity,
                    newLastSuccessfulCallUtc);

                var analysisResult = await App.RecentStyleClient.AnalyzeAsync(characterId);
                recentStyle = analysisResult.RecentStyle;
                threatBand = analysisResult.ThreatBand;
                engineFailureReason = analysisResult.FailureReason;

                if (engineFailureReason is null)
                {
                    if (recentCallFailed && killmailDerivedActivity?.HasPublicActivityData != true)
                        recentStyle = StyleClassification.Unknown;

                    if (statisticsCallFailed)
                        threatBand = "Unk";
                }
            }

            rows.Add(PilotReportRowFactory.FromPilot(
                pilot,
                activity,
                statistics,
                recentStyle,
                threatBand,
                statisticsCallFailed,
                recentCallFailed,
                engineFailureReason));
        }

        if (rows.Count == 0)
            return;

        _viewModel.Pilots.Clear();

        foreach (var row in rows)
            _viewModel.Pilots.Add(row);
    }

    private static async Task<zKillActivity?> LoadDerivedActivityAsync(long characterId)
    {
        try
        {
            return await App.RecentKillmailCache.GetDerivedActivityAsync(characterId);
        }
        catch
        {
            return new zKillActivity(
                characterId,
                false,
                null,
                null,
                null,
                null,
                ApplicationClock.UtcNow,
                "Recent killmail activity derivation failed.");
        }
    }

    private static async Task<(zKillStatistics? Statistics, bool FetchedThisScan)> LoadzKillStatisticsAsync(long? characterId)
    {
        if (characterId is null)
            return (null, false);

        try
        {
            var cached = await App.zKillStatisticsCache.GetAsync(
                characterId.Value,
                CacheDurations.zKillStatistics);

            if (cached is not null)
                return (cached, false);
        }
        catch
        {
            // Cache failures should not prevent live zKill stats lookup.
        }

        var result = await App.zKillClient.GetStatisticsAsync(characterId.Value);

        var statistics = result.Outcome switch
        {
            zKillStatisticsOutcome.Success => result.Statistics,
            zKillStatisticsOutcome.NoHistory => new zKillStatistics { NoHistory = true },
            _ => null
        };

        if (statistics is null)
            return (null, true);

        try
        {
            var style = StyleDisplayFormatter.Format(GeneralStyleClassifier.Classify(statistics));
            await App.zKillStatisticsCache.UpsertAsync(
                characterId.Value,
                statistics,
                style,
                noHistory: statistics.NoHistory);
        }
        catch
        {
            // Live zKill stats should still be displayed even if caching fails.
        }

        return (statistics, true);
    }

    private static async Task<zKillActivity?> SafeGetStoredActivityAsync(long characterId)
    {
        try
        {
            return await App.zKillActivityCache.GetAsync(characterId);
        }
        catch
        {
            return null;
        }
    }

    private static async Task<zKillActivity?> ResolveActivityAsync(
        long characterId,
        zKillStatistics? statistics,
        zKillActivity? stored,
        zKillActivity? killmailDerived,
        DateTimeOffset? newLastSuccessfulCallUtc)
    {
        try
        {
            var (statisticsLastActiveUtc, statisticsActivityType) =
                LastActiveDeriver.Derive(statistics?.months, ApplicationClock.UtcNow);

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

            var lastSuccessfulRecentCallUtc = newLastSuccessfulCallUtc ?? stored?.LastSuccessfulRecentCallUtc;

            var hasPublicActivityData = lastActiveUtc is not null
                || (stored?.HasPublicActivityData ?? false)
                || (killmailDerived?.HasPublicActivityData ?? false);

            if (stored is null && !hasPublicActivityData && lastSuccessfulRecentCallUtc is null)
                return killmailDerived;

            var merged = new zKillActivity(
                characterId,
                hasPublicActivityData,
                killmailDerived?.KillsWeek ?? stored?.KillsWeek,
                killmailDerived?.SoloWeek ?? stored?.SoloWeek,
                lastActiveUtc,
                lastActivityType,
                ApplicationClock.UtcNow,
                killmailDerived?.Error,
                lastSuccessfulRecentCallUtc);

            await App.zKillActivityCache.UpsertAsync(merged);

            return merged;
        }
        catch
        {
            return killmailDerived;
        }
    }

    private static async Task<(DateTimeOffset? NewLastSuccessfulCallUtc, bool Failed)> RefreshRecentKillmailsAsync(
        long characterId,
        zKillActivity? storedActivity,
        zKillStatistics? statistics,
        bool statisticsFetchedThisScan)
    {
        try
        {
            await App.RecentKillmailCache.RemoveExpiredAsync();

            var now = ApplicationClock.UtcNow;
            var lastSuccessfulCallUtc = storedActivity?.LastSuccessfulRecentCallUtc;

            if (RecentCallScheduler.ShouldSkipForInterval(lastSuccessfulCallUtc, now))
                return (null, false);

            var noHistory = statistics?.NoHistory ?? false;

            if (!noHistory
                && statisticsFetchedThisScan
                && RecentCallScheduler.ShouldShortCircuit(statistics?.months, now))
            {
                return (now, false);
            }

            var pastSeconds = RecentCallScheduler.CalculatePastSeconds(lastSuccessfulCallUtc, now);
            var result = await App.zKillClient.GetRecentKillmailsAsync(characterId, pastSeconds);

            switch (result.Outcome)
            {
                case zKillRecentKillmailOutcome.Success:
                    if (result.Killmails.Count > 0)
                    {
                        await App.RecentKillmailCache.UpsertAsync(result.Killmails);
                        await App.zKillStatisticsCache.ClearNoHistoryMarkerAsync(characterId);
                    }

                    return (now, false);

                case zKillRecentKillmailOutcome.NoHistory:
                    return (now, false);

                default:
                    return (null, true);
            }
        }
        catch
        {
            // Recent killmail caching must not break the visible report.
            return (null, true);
        }
    }

    private void Diagnostics_Click(object sender, RoutedEventArgs e)
    {
        var window = new DiagnosticsWindow
        {
            Owner = this
        };

        window.Show();
    }

    private void ResetDeveloperClock_Click(object sender, RoutedEventArgs e)
    {
        ApplicationClock.Reset();
    }

#if HISTORIC_RELATIONSHIPS
    private void GroupDetectionHistoryPilot_Click(object sender, RoutedEventArgs e)
    {
        var window = new GroupDetectionHistoryPilotWindow
        {
            Owner = this
        };

        window.ShowDialog();
    }
#endif
}
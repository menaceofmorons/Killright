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

            if (pilot.CharacterId is not null)
            {
                var characterId = pilot.CharacterId.Value;

                statistics = await LoadzKillStatisticsAsync(characterId);

                var fallbackActivity = await RefreshRecentKillmailsAsync(characterId);
                var killmailDerivedActivity = await LoadDerivedActivityAsync(characterId, fallbackActivity);

                activity = await ResolveActivityAsync(characterId, statistics, killmailDerivedActivity);

                var analysisResult = await App.RecentStyleClient.AnalyzeAsync(characterId);
                recentStyle = analysisResult.RecentStyle;
                threatBand = analysisResult.ThreatBand;

                if (recentStyle == StyleClassification.Unknown && killmailDerivedActivity?.HasPublicActivityData == true)
                    recentStyle = StyleClassification.Inactive;
            }

            rows.Add(PilotReportRowFactory.FromPilot(
                pilot,
                activity,
                statistics,
                recentStyle,
                threatBand));
        }

        if (rows.Count == 0)
            return;

        _viewModel.Pilots.Clear();

        foreach (var row in rows)
            _viewModel.Pilots.Add(row);
    }

    private static async Task<zKillActivity?> LoadDerivedActivityAsync(
        long characterId,
        zKillActivity? fallbackActivity)
    {
        try
        {
            var cachedActivity = await App.RecentKillmailCache.GetDerivedActivityAsync(characterId);

            if (cachedActivity.HasPublicActivityData)
                return cachedActivity;

            return fallbackActivity ?? cachedActivity;
        }
        catch
        {
            return fallbackActivity ?? new zKillActivity(
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

    private static async Task<zKillStatistics?> LoadzKillStatisticsAsync(long? characterId)
    {
        if (characterId is null)
            return null;

        try
        {
            var cached = await App.zKillStatisticsCache.GetAsync(
                characterId.Value,
                CacheDurations.zKillStatistics);

            if (cached is not null)
                return cached;
        }
        catch
        {
            // Cache failures should not prevent live zKill stats lookup.
        }

        var result = await App.zKillClient.GetStatisticsAsync(characterId.Value);

        var statistics = result.Outcome switch
        {
            zKillStatisticsOutcome.Success => result.Statistics,
            zKillStatisticsOutcome.NoHistory => new zKillStatistics(),
            _ => null
        };

        if (statistics is null)
            return null;

        try
        {
            var style = StyleDisplayFormatter.Format(GeneralStyleClassifier.Classify(statistics));
            await App.zKillStatisticsCache.UpsertAsync(
                characterId.Value,
                statistics,
                style,
                noHistory: result.Outcome == zKillStatisticsOutcome.NoHistory);
        }
        catch
        {
            // Live zKill stats should still be displayed even if caching fails.
        }

        return statistics;
    }

    private static async Task<zKillActivity?> ResolveActivityAsync(
        long characterId,
        zKillStatistics? statistics,
        zKillActivity? killmailDerived)
    {
        try
        {
            var stored = await App.zKillActivityCache.GetAsync(characterId);
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

            var hasPublicActivityData = lastActiveUtc is not null
                || (stored?.HasPublicActivityData ?? false)
                || (killmailDerived?.HasPublicActivityData ?? false);

            if (stored is null && !hasPublicActivityData)
                return killmailDerived;

            var merged = new zKillActivity(
                characterId,
                hasPublicActivityData,
                killmailDerived?.KillsWeek ?? stored?.KillsWeek,
                killmailDerived?.SoloWeek ?? stored?.SoloWeek,
                lastActiveUtc,
                lastActivityType,
                ApplicationClock.UtcNow,
                killmailDerived?.Error);

            await App.zKillActivityCache.UpsertAsync(merged);

            return merged;
        }
        catch
        {
            return killmailDerived;
        }
    }

    private static async Task<zKillActivity?> RefreshRecentKillmailsAsync(long characterId)
    {
        try
        {
            await App.RecentKillmailCache.RemoveExpiredAsync();
            var latestKillmailUtc = await App.RecentKillmailCache.GetMostRecentKillmailAsync(characterId);
            var pastSeconds = CalculatePastSeconds(latestKillmailUtc);
            var recent = await App.zKillClient.GetRecentKillmailsAsync(characterId, pastSeconds);

            if (recent.Count > 0)
            {
                await App.RecentKillmailCache.UpsertAsync(recent);
                return null;
            }

            return await App.zKillClient.GetLatestActivityAsync(characterId);
        }
        catch
        {
            // Recent killmail caching must not break the visible report.
            return null;
        }
    }

    private static int CalculatePastSeconds(DateTimeOffset? latestKillmailUtc)
    {
        const int sevenDays = 7 * 24 * 60 * 60;
        const int minimumWindowSeconds = 3600;
        const int overlapSeconds = 300;

        if (latestKillmailUtc is null)
            return sevenDays;

        var requiredSeconds =
            (int)Math.Ceiling(
                (ApplicationClock.UtcNow - latestKillmailUtc.Value)
                    .TotalSeconds) + overlapSeconds;

        var hours =
            (int)Math.Ceiling(
                requiredSeconds / 3600d);

        return Math.Max(
            minimumWindowSeconds,
            hours * 3600);
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
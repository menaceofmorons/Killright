using System.Windows;
using System.Windows.Interop;
using PilotIntel.Core.Style;
using PilotIntel.Integration.zKill;
using PilotIntel.Shared;
using PilotIntel.Shared.Constants;
using PilotIntel.Shared.Time;
using PilotIntel.Shared.zKill;
using PilotIntel.UI.ClipboardMonitoring;
using PilotIntel.UI.Diagnostics;
using PilotIntel.UI.ViewModels;

namespace PilotIntel.UI;

public partial class MainWindow : Window
{
    private readonly MainWindowViewModel _viewModel;
    private ClipboardMonitor? _clipboardMonitor;

    public MainWindow()
    {
        InitializeComponent();
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
        if (!System.Windows.Clipboard.ContainsText())
            return;

        var text = System.Windows.Clipboard.GetText();
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

            if (pilot.VerifyStatus == VerifyStatus.NoMatch)
                continue;

            zKillActivity? activity = null;
            var recentStyle = StyleClassification.Unknown;
            var threatBand = "Unk";

            if (pilot.CharacterId is not null)
            {
                await RefreshRecentKillmailsAsync(pilot.CharacterId.Value);

                activity = await LoadDerivedActivityAsync(pilot.CharacterId.Value);

                var analysisResult = await App.RecentStyleClient.AnalyzeAsync(
                    pilot.CharacterId.Value);

                recentStyle = analysisResult.RecentStyle;
                threatBand = analysisResult.ThreatBand;
            }

            var statistics = await LoadzKillStatisticsAsync(pilot.CharacterId);

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
        {
            _viewModel.Pilots.Add(row);
        }
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

        var statistics = await App.zKillClient.GetStatisticsAsync(characterId.Value);

        if (statistics is null)
            return null;

        try
        {
            var style = StyleDisplayFormatter.Format(
                GeneralStyleClassifier.Classify(statistics));

            await App.zKillStatisticsCache.UpsertAsync(
                characterId.Value,
                statistics,
                style);
        }
        catch
        {
            // Live zKill stats should still be displayed even if caching fails.
        }

        return statistics;
    }

    private static async Task RefreshRecentKillmailsAsync(long characterId)
    {
        try
        {
            await App.RecentKillmailCache.RemoveExpiredAsync();

            var latestKillmailUtc = await App.RecentKillmailCache.GetMostRecentKillmailAsync(characterId);
            var pastSeconds = CalculatePastSeconds(latestKillmailUtc);
            var recent = await App.zKillClient.GetRecentKillmailsAsync(characterId, pastSeconds);

            await App.RecentKillmailCache.UpsertAsync(recent);
        }
        catch
        {
            // Recent killmail caching must not break the visible report.
        }
    }

    // zKill pastSeconds requests must be supplied as whole-hour multiples (3600 seconds).
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
}
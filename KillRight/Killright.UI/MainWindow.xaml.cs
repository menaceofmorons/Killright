using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Killright.Core.Activity;
using Killright.Core.Style;
using Killright.Integration.zKill;
using Killright.Shared;
using Killright.Shared.Constants;
using Killright.Shared.Time;
using Killright.Shared.zKill;
using Killright.Storage.Killmails;
using Killright.UI.ClipboardMonitoring;
using Killright.UI.InfoSheet;
using Killright.UI.Interop;
using Killright.UI.MenuModal;
using Killright.UI.Shortcuts;
using Killright.UI.UiState;
using Killright.UI.ViewModels;

namespace Killright.UI;

public partial class MainWindow : Window
{
    private readonly MainWindowViewModel _viewModel;
    private readonly Dictionary<string, DataGridColumn> _columnsById;
    private ClipboardMonitor? _clipboardMonitor;
    private bool _menuModalOpen;
    private InfoSheetWindow? _infoSheet;

    public MainWindow()
    {
        InitializeComponent();
        _viewModel = new MainWindowViewModel();
        DataContext = _viewModel;

        var initial = App.UiState.Current;
        Topmost = initial.AlwaysOnTop;
        Width = initial.WindowWidth;
        Height = initial.WindowHeight;

        if (App.UiState.IsUsingDefaults)
        {
            var workArea = SystemParameters.WorkArea;
            (Left, Top) = WindowBoundsCalculator.CenterOn(
                workArea.Left,
                workArea.Top,
                workArea.Width,
                workArea.Height,
                initial.WindowWidth,
                initial.WindowHeight);
        }
        else
        {
            Left = initial.WindowLeft;
            Top = initial.WindowTop;
        }

        _columnsById = new Dictionary<string, DataGridColumn>
        {
            [ColumnIds.Pilot] = ColumnPilot,
            [ColumnIds.Verify] = ColumnVerify,
            [ColumnIds.Threat] = ColumnThreat,
            [ColumnIds.SecurityStatus] = ColumnSecurityStatus,
            [ColumnIds.Group] = ColumnGroup,
            [ColumnIds.Corporation] = ColumnCorporation,
            [ColumnIds.Alliance] = ColumnAlliance,
            [ColumnIds.Style] = ColumnStyle,
            [ColumnIds.Week] = ColumnWeek,
            [ColumnIds.LastActive] = ColumnLastActive,
            [ColumnIds.Notes] = ColumnNotes
        };

        ApplyPreviewColumns(initial);
        HookColumnLiveUpdateEvents();

        LocationChanged += MainWindow_LocationChanged;
        SizeChanged += MainWindow_SizeChanged;
    }

    internal void ApplyPreviewBounds(UiStateModel state)
    {
        Topmost = state.AlwaysOnTop;
        Width = state.WindowWidth;
        Height = state.WindowHeight;
        Left = state.WindowLeft;
        Top = state.WindowTop;
    }

    internal void ApplyPreviewColumns(UiStateModel state)
    {
        foreach (var columnState in state.Columns.OrderBy(column => column.DisplayIndex))
        {
            if (!_columnsById.TryGetValue(columnState.Id, out var column))
                continue;

            column.DisplayIndex = Math.Min(columnState.DisplayIndex, PilotGrid.Columns.Count - 1);
            column.Width = new DataGridLength(columnState.Width);
            column.Visibility = UiStateDefaults.IsEffectivelyVisible(columnState, state.DeveloperTabRevealed)
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        ResetSortIfHiddenColumnIsSorted();
    }

    private void HookColumnLiveUpdateEvents()
    {
        foreach (var column in _columnsById.Values)
        {
            DependencyPropertyDescriptor
                .FromProperty(DataGridColumn.WidthProperty, typeof(DataGridColumn))
                .AddValueChanged(column, (_, _) => PersistColumnLayout());
        }
    }

    private void PilotGrid_ColumnDisplayIndexChanged(object? sender, DataGridColumnEventArgs e)
    {
        PersistColumnLayout();
    }

    private void PersistColumnLayout()
    {
        // Width/DisplayIndex changes (dragging a column border or header) fire
        // this via HookColumnLiveUpdateEvents/PilotGrid_ColumnDisplayIndexChanged
        // even while a Developer-gated column's rendered Visibility only
        // reflects the *effective* (gate-dependent) state, not the user's raw
        // checkbox preference (§IsEffectivelyVisible). Deriving Visible from
        // pair.Value.Visibility here would silently overwrite that preference
        // -- e.g. collapsing Notes/Verify back to unchecked the moment the
        // Developer tab is hidden and any column is resized. Preserve the
        // existing stored preference instead; HideColumn is the only place
        // that deliberately changes a column's persisted visibility.
        var previousVisibleById = App.UiState.Current.Columns.ToDictionary(column => column.Id, column => column.Visible);

        var snapshot = _columnsById
            .Select(pair => new ColumnState
            {
                Id = pair.Key,
                DisplayIndex = pair.Value.DisplayIndex,
                Width = pair.Value.Width.Value,
                Visible = previousVisibleById.GetValueOrDefault(pair.Key)
            })
            .OrderBy(columnState => columnState.DisplayIndex)
            .ToList();

        App.UiState.UpdateColumns(snapshot);
    }

    private void PilotGrid_PreviewMouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (FindAncestor<DataGridColumnHeader>(e.OriginalSource as DependencyObject) is { Column: { } column } header)
        {
            var columnId = _columnsById.FirstOrDefault(pair => pair.Value == column).Key;

            if (columnId is null)
                return;

            var hideItem = new MenuItem { Header = "Hide", IsEnabled = columnId != ColumnIds.Pilot };
            hideItem.Click += (_, _) => HideColumn(columnId);

            var contextMenu = new ContextMenu();
            contextMenu.Items.Add(hideItem);
            header.ContextMenu = contextMenu;
            contextMenu.IsOpen = true;
            return;
        }

        if (FindAncestor<DataGridCell>(e.OriginalSource as DependencyObject) is { Column: var cellColumn } cell
            && cellColumn == ColumnPilot
            && FindAncestor<DataGridRow>(cell) is { Item: PilotReportRow row })
        {
            OpenInfoSheet(row, PointToScreen(e.GetPosition(this)));
        }
    }

    private void OpenInfoSheet(PilotReportRow row, Point screenPosition)
    {
        _infoSheet?.Close();

        var dpi = VisualTreeHelper.GetDpi(this);
        var infoSheet = new InfoSheetWindow(row, App.UiState.Current.DeveloperTabRevealed)
        {
            Owner = this,
            Topmost = Topmost,
            Left = screenPosition.X / dpi.DpiScaleX,
            Top = screenPosition.Y / dpi.DpiScaleY
        };

        infoSheet.Closed += (_, _) => _infoSheet = null;
        _infoSheet = infoSheet;
        infoSheet.Show();
    }

    private void HideColumn(string columnId)
    {
        if (columnId == ColumnIds.Pilot || !_columnsById.TryGetValue(columnId, out var column))
            return;

        column.Visibility = Visibility.Collapsed;
        ResetSortIfHiddenColumnIsSorted();

        var updatedColumns = App.UiState.Current.Columns
            .Select(columnState => columnState.Id == columnId ? columnState with { Visible = false } : columnState)
            .ToList();
        App.UiState.UpdateColumns(updatedColumns);
    }

    private void ResetSortIfHiddenColumnIsSorted()
    {
        if (PilotGrid.Items.SortDescriptions.Count == 0)
            return;

        var sortedPropertyName = PilotGrid.Items.SortDescriptions[0].PropertyName;
        var sortedColumn = _columnsById.Values.FirstOrDefault(column => GetBindingPath(column) == sortedPropertyName);

        if (sortedColumn is null || sortedColumn.Visibility == Visibility.Visible)
            return;

        PilotGrid.Items.SortDescriptions.Clear();

        foreach (var column in _columnsById.Values)
            column.SortDirection = null;

        PilotGrid.Items.SortDescriptions.Add(new SortDescription(GetBindingPath(ColumnPilot), ListSortDirection.Ascending));
        ColumnPilot.SortDirection = ListSortDirection.Ascending;
    }

    private static string GetBindingPath(DataGridColumn column) => column switch
    {
        DataGridBoundColumn bound => ((Binding)bound.Binding).Path.Path,
        DataGridTemplateColumn template => template.SortMemberPath ?? string.Empty,
        _ => string.Empty
    };

    private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
    {
        while (current is not null)
        {
            if (current is T match)
                return match;

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        var handle = new WindowInteropHelper(this).Handle;
        WindowStyleInterop.RemoveMaximizeBox(handle);

        _clipboardMonitor = new ClipboardMonitor(handle);
        _clipboardMonitor.ClipboardChanged += ClipboardChanged;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        App.UiState.SaveNow();
    }

    private void MainWindow_LocationChanged(object? sender, EventArgs e)
    {
        if (_menuModalOpen)
            return;

        App.UiState.UpdateBounds(Left, Top, Width, Height);
    }

    private void MainWindow_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_menuModalOpen)
            return;

        App.UiState.UpdateBounds(Left, Top, Width, Height);
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.System && e.SystemKey is Key.LeftAlt or Key.RightAlt)
        {
            e.Handled = true;
            return;
        }

        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.M)
        {
            e.Handled = true;
            OpenMenuModal();
        }
        else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.Q)
        {
            e.Handled = true;
            Close();
        }
        else if (e.Key == Key.F1)
        {
            e.Handled = true;
            new ShortcutsWindow { Owner = this }.ShowDialog();
        }
    }

    private void Window_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (Keyboard.Modifiers == ModifierKeys.Alt)
        {
            e.Handled = true;
            DragMove();
        }
    }

    private void OpenMenuModal()
    {
        if (_menuModalOpen)
            return;

        _menuModalOpen = true;

        try
        {
            new MenuModalWindow(this) { Owner = this }.ShowDialog();
        }
        finally
        {
            _menuModalOpen = false;
        }
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
            PilotRecentKillmail? lastActivity = null;
            var birthday = await SafeGetBirthdayAsync(pilotName);

            if (pilot.CharacterId is not null)
            {
                var characterId = pilot.CharacterId.Value;

                var (loadedStatistics, statisticsFetchedThisScan) = await LoadzKillStatisticsAsync(characterId);
                statistics = loadedStatistics;
                statisticsCallFailed = statisticsFetchedThisScan && statistics is null;

                var storedActivity = await SafeGetStoredActivityAsync(characterId);

                var (newLastSuccessfulCallUtc, recentCallDidFail, pastSecondsRequested) = await RefreshRecentKillmailsAsync(
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
                    newLastSuccessfulCallUtc,
                    pastSecondsRequested);

                lastActivity = await LoadMostRecentKillmailAsync(characterId);

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
                engineFailureReason,
                birthday,
                lastActivity));
        }

        if (rows.Count == 0)
            return;

        await AttachGroupRelationshipsAsync(rows);
        PilotGroupCountAnnotator.Annotate(rows, App.Settings.NpcCorporationIdThreshold);

        _viewModel.Pilots.Clear();

        foreach (var row in rows)
            _viewModel.Pilots.Add(row);
    }

    private static async Task AttachGroupRelationshipsAsync(List<PilotReportRow> rows)
    {
        var scannedCharacterIds = rows
            .Where(row => row.CharacterId is not null)
            .Select(row => row.CharacterId!.Value)
            .Distinct()
            .ToList();

        if (scannedCharacterIds.Count < 2)
            return;

        var groupResult = await App.RecentStyleClient.AnalyzeGroupAsync(scannedCharacterIds);

        foreach (var row in rows)
        {
            if (row.CharacterId is null)
                continue;

            row.GroupRelationships = groupResult.ForCharacter(row.CharacterId.Value).ToList();
        }
    }

    private static async Task<DateOnly?> SafeGetBirthdayAsync(string pilotName)
    {
        try
        {
            return await App.PilotIdentityCache.GetBirthdayAsync(pilotName);
        }
        catch
        {
            return null;
        }
    }

    private static async Task<PilotRecentKillmail?> LoadMostRecentKillmailAsync(long characterId)
    {
        try
        {
            return await App.RecentKillmailCache.GetMostRecentKillmailAsync(characterId);
        }
        catch
        {
            return null;
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
        DateTimeOffset? newLastSuccessfulCallUtc,
        int? pastSecondsRequested)
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
            var recentCoverageStartUtc = ResolveCoverageStartUtc(stored, pastSecondsRequested);

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
                lastSuccessfulRecentCallUtc,
                recentCoverageStartUtc);

            await App.zKillActivityCache.UpsertAsync(merged);

            return merged;
        }
        catch
        {
            return killmailDerived;
        }
    }

    private static DateTimeOffset? ResolveCoverageStartUtc(zKillActivity? stored, int? pastSecondsRequested)
    {
        return RecentCallScheduler.ResolveCoverageStartUtc(
            stored?.RecentCoverageStartUtc,
            stored?.LastSuccessfulRecentCallUtc,
            pastSecondsRequested,
            ApplicationClock.UtcNow);
    }

    private static async Task<(DateTimeOffset? NewLastSuccessfulCallUtc, bool Failed, int? PastSecondsRequested)> RefreshRecentKillmailsAsync(
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
                return (null, false, null);

            var noHistory = statistics?.NoHistory ?? false;

            if (!noHistory
                && statisticsFetchedThisScan
                && RecentCallScheduler.ShouldShortCircuit(statistics?.months, now))
            {
                return (now, false, null);
            }

            var pastSeconds = RecentCallScheduler.CalculatePastSeconds(lastSuccessfulCallUtc, now);
            var result = await App.zKillClient.GetRecentKillmailsAsync(characterId, pastSeconds);

            switch (result.Outcome)
            {
                case zKillRecentKillmailOutcome.Success:
                    if (result.RawKillmails.Count > 0)
                    {
                        await App.KillmailStore.UpsertAsync(characterId, result.RawKillmails);
                        await App.zKillStatisticsCache.ClearNoHistoryMarkerAsync(characterId);
                    }

                    return (now, false, pastSeconds);

                case zKillRecentKillmailOutcome.NoHistory:
                    return (now, false, pastSeconds);

                default:
                    return (null, true, null);
            }
        }
        catch
        {
            // Recent killmail caching must not break the visible report.
            return (null, true, null);
        }
    }

}
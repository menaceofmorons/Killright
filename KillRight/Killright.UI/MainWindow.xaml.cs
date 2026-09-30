using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Killright.Core.Activity;
using Killright.Core.Models;
using Killright.Core.Style;
using Killright.Integration.zKill;
using Killright.Shared;
using Killright.Shared.Constants;
using Killright.Shared.Time;
using Killright.Shared.zKill;
using Killright.Storage.Database;
using Killright.Storage.Diagnostics;
using Killright.Storage.Killmails;
using Killright.Storage.Scan;
using Killright.UI.Analysis;
using Killright.UI.ClipboardMonitoring;
using Killright.UI.Configuration;
using Killright.UI.Diagnostics;
using Killright.UI.InfoSheet;
using Killright.UI.Interop;
using Killright.UI.MenuModal;
using Killright.UI.Resources;
using Killright.UI.Scan;
using Killright.UI.Shortcuts;
using Killright.UI.Style;
using Killright.UI.UiState;
using Killright.UI.ViewModels;

namespace Killright.UI;

public partial class MainWindow : Window
{
    private const int HoverDelayMilliseconds = 150;

    private readonly MainWindowViewModel _viewModel;
    private readonly Dictionary<string, DataGridColumn> _columnsById;
    private readonly DispatcherTimer _hoverTimer;
    private readonly ScanCoordinator _scanCoordinator;
    private readonly IReadOnlyList<RelationshipConfidenceBandSetting> _relationshipConfidenceBands;
    private ClipboardMonitor? _clipboardMonitor;
    private bool _menuModalOpen;
    private InfoSheetWindow? _infoSheet;
    private PilotReportRow? _pendingHoverRow;

    public MainWindow()
    {
        InitializeComponent();
        _viewModel = new MainWindowViewModel();
        DataContext = _viewModel;

        _relationshipConfidenceBands = RelationshipConfidenceBandSetting.ValidateOrDefault(App.Settings.RelationshipConfidenceBands);
        _hoverTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(HoverDelayMilliseconds) };
        _hoverTimer.Tick += HoverTimer_Tick;

        _scanCoordinator = new ScanCoordinator(
            ResolvePilotsTimedAsync,
            App.PurgeScheduler.ScanStarted,
            () =>
            {
                App.PurgeScheduler.ScanFinished();
                _ = App.PurgeScheduler.RunPostScanPassAsync();
            },
            exception => EngineFailureLog.Record($"scan failed: {exception}"));

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
            [ColumnIds.Relationship] = ColumnRelationship,
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

            var hideItem = new MenuItem { Header = UiText.GridContextMenuHide, IsEnabled = columnId != ColumnIds.Pilot };
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

    private void PilotGrid_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount != 2)
            return;

        if (FindAncestor<DataGridCell>(e.OriginalSource as DependencyObject) is not { Column: var cellColumn } cell
            || cellColumn != ColumnPilot
            || FindAncestor<DataGridRow>(cell) is not { Item: PilotReportRow row })
            return;

        e.Handled = true;

        if (row.CharacterId is null)
            return;

        Process.Start(new ProcessStartInfo($"https://zkillboard.com/character/{row.CharacterId.Value}/") { UseShellExecute = true });
    }

    private void PilotCell_MouseEnter(object sender, MouseEventArgs e)
    {
        if (sender is not DataGridCell { DataContext: PilotReportRow row })
            return;

        _pendingHoverRow = row;
        _hoverTimer.Stop();
        _hoverTimer.Start();
    }

    private void PilotCell_MouseLeave(object sender, MouseEventArgs e)
    {
        _hoverTimer.Stop();
        _pendingHoverRow = null;
        RelationshipHighlightCalculator.Clear(_viewModel.Pilots);
    }

    private void HoverTimer_Tick(object? sender, EventArgs e)
    {
        _hoverTimer.Stop();

        var pilotColor = ParseColor(App.UiState.Current.PilotHighlightColorHex, UiStateDefaults.PilotHighlightColorHex);
        var relatedColor = ParseColor(App.UiState.Current.RelatedHighlightColorHex, UiStateDefaults.RelatedHighlightColorHex);

        RelationshipHighlightCalculator.Apply(
            _viewModel.Pilots,
            _pendingHoverRow,
            App.SdeReferenceDataStore.IsNpcCorporation,
            _relationshipConfidenceBands,
            pilotColor,
            relatedColor,
            App.Settings.HighlightOpacity);
    }

    private static Color ParseColor(string hex, string fallbackHex)
    {
        try
        {
            return (Color)ColorConverter.ConvertFromString(hex)!;
        }
        catch
        {
            return (Color)ColorConverter.ConvertFromString(fallbackHex)!;
        }
    }

    private void OpenInfoSheet(PilotReportRow row, Point screenPosition)
    {
        _infoSheet?.Close();

        var dpi = VisualTreeHelper.GetDpi(this);
        var lookupTag = new LastActivityLookupTag();
        var viewModel = new InfoSheetViewModel(
            row,
            App.UiState.Current.DeveloperTabRevealed,
            LoadBirthdayForInfoSheetAsync,
            characterId => LoadLastActivityForInfoSheetAsync(characterId, lookupTag));

        var infoSheet = new InfoSheetWindow(viewModel)
        {
            Owner = this,
            Topmost = Topmost,
            Left = screenPosition.X / dpi.DpiScaleX,
            Top = screenPosition.Y / dpi.DpiScaleY
        };

        infoSheet.Closed += (_, _) => _infoSheet = null;
        _infoSheet = infoSheet;
        infoSheet.Show();

        _ = LoadInfoSheetAsync(viewModel, row, lookupTag);
    }

    private static Task<DateOnly?> LoadBirthdayForInfoSheetAsync(string inputName)
    {
        return Task.Run(() => App.PilotIdentityCache.GetBirthdayAsync(inputName));
    }

    private static Task<PilotLastActivitySummary?> LoadLastActivityForInfoSheetAsync(long characterId, LastActivityLookupTag lookupTag)
    {
        return Task.Run(async () =>
        {
            try
            {
                var resolution = await App.LastActivityResolver.ResolveAsync(characterId);
                lookupTag.Value = resolution.Source == PilotLastActivitySource.Live ? "live" : "cache";
                return PilotLastActivitySummaryBuilder.Build(resolution.Killmail, App.SdeReferenceDataStore);
            }
            catch
            {
                lookupTag.Value = "failed";
                throw;
            }
        });
    }

    private sealed class LastActivityLookupTag
    {
        public string? Value { get; set; }
    }

    private static async Task LoadInfoSheetAsync(InfoSheetViewModel viewModel, PilotReportRow row, LastActivityLookupTag lookupTag)
    {
        var timings = App.Settings.Timing.Enabled ? new ScanTimings() : null;
        var zKillRequestsAtStart = App.zKillClient.RequestCount;

        using (var scope = timings.Measure(ScanTimings.ScanLevel, "popup_load", row.CharacterId))
        {
            await viewModel.LoadAsync();
            scope.Tag = lookupTag.Value;
        }

        timings.Add(ScanTimings.ScanLevel, ScanTimings.CounterPrefix + "zkill_requests", App.zKillClient.RequestCount - zKillRequestsAtStart, row.CharacterId);

        try
        {
            timings?.Flush();
        }
        catch
        {
        }
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
        _scanCoordinator.CancelCurrent();
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

    private void ClipboardChanged(object? sender, EventArgs e)
    {
        var timings = App.Settings.Timing.Enabled ? new ScanTimings() : null;
        string text;

        try
        {
            if (!System.Windows.Clipboard.ContainsText())
                return;

            using (timings.Measure(ScanTimings.ScanLevel, "clipboard_read"))
            {
                text = System.Windows.Clipboard.GetText();
            }
        }
        catch
        {
            // Clipboard access can transiently fail (COMException,
            // CLIPBRD_E_CANT_OPEN) whenever another process briefly holds
            // the clipboard open -- not worth crashing the app over. The
            // next clipboard change will try again (Step CC-12.08.26.02).
            return;
        }

        IReadOnlyList<string> pilotNames;

        using (timings.Measure(ScanTimings.ScanLevel, "clipboard_parse"))
        {
            pilotNames = PilotListParser.ParseIfLikelyPilotList(text);
        }

        if (pilotNames.Count == 0)
            return;

        _ = _scanCoordinator.Submit(pilotNames, timings);
    }

    internal ObservableCollection<PilotReportRow> Pilots => _viewModel.Pilots;

    private async Task ResolvePilotsTimedAsync(IReadOnlyList<string> pilotNames, ScanTimings? timings, ScanContext context)
    {
        timings ??= App.Settings.Timing.Enabled ? new ScanTimings() : null;

        if (timings is null)
        {
            await ResolvePilotsCoreAsync(pilotNames, null, context);
            return;
        }

        timings.PilotCount = pilotNames.Count;
        timings.Record(ScanTimings.ScanLevel, "on_ui_thread_at_start", Dispatcher.CheckAccess() ? 1 : 0);
        var stallMonitor = await Dispatcher.InvokeAsync(() => UiStallMonitor.Start(Dispatcher));

        try
        {
            await ResolvePilotsCoreAsync(pilotNames, timings, context);
        }
        finally
        {
            try
            {
                if (context.Token.IsCancellationRequested)
                    timings.Record(ScanTimings.ScanLevel, "scan_cancelled", 1);

                timings.Record(ScanTimings.ScanLevel, "ui_max_stall_ms", await Dispatcher.InvokeAsync(stallMonitor.Stop));
                timings.Record(ScanTimings.ScanLevel, "scan_total", timings.ElapsedMilliseconds);
                timings.Flush();
            }
            catch
            {
            }
        }
    }

    private async Task ResolvePilotsCoreAsync(IReadOnlyList<string> pilotNames, ScanTimings? timings, ScanContext context)
    {
        var cancellationToken = context.Token;
        var ignoreList = App.UiState.Current.IgnoreListEntries;
        var esiRequestsAtStart = App.EsiClient.RequestCount;
        var zKillRequestsAtStart = App.zKillClient.RequestCount;
        var limiterWaitAtStart = App.zKillRequestLimiter.TotalWaitMilliseconds;
        var writes = new ScanWriteBatch();

        IReadOnlyList<Pilot> pilots;
        PilotNetworkResult[] networkResults;

        var session = TryOpenScanSession();

        try
        {
            IReadOnlyList<Pilot> resolvedPilots;

            using (timings.Measure(ScanTimings.ScanLevel, "identity_stage"))
            {
                resolvedPilots = await App.IdentityResolver.ResolveAsync(pilotNames, timings, cancellationToken, session, writes);
            }

            cancellationToken.ThrowIfCancellationRequested();

            pilots = ScanPilotSelector.SelectForAnalysis(resolvedPilots, ignoreList);

            if (pilots.Count == 0)
                return;

            IReadOnlyDictionary<long, zKillStatistics> cachedStatistics;
            IReadOnlyDictionary<long, zKillActivity> storedActivities;

            using (timings.Measure(ScanTimings.ScanLevel, "cache_read"))
            {
                var characterIds = pilots
                    .Where(pilot => pilot.CharacterId is not null)
                    .Select(pilot => pilot.CharacterId!.Value)
                    .Distinct()
                    .ToList();

                cachedStatistics = await ReadCachedStatisticsAsync(characterIds, session);
                storedActivities = await ReadStoredActivitiesAsync(characterIds, session);
            }

            cancellationToken.ThrowIfCancellationRequested();

            using (timings.Measure(ScanTimings.ScanLevel, "zkill_stage"))
            {
                networkResults = await BoundedConcurrentRunner.RunAsync(
                    pilots,
                    App.Settings.Network.MaxConcurrency,
                    (pilot, position, token) => FetchPilotNetworkAsync(pilot, position, cachedStatistics, storedActivities, writes, timings, token),
                    (_, _) => PilotNetworkResult.Failed,
                    cancellationToken);
            }

            cancellationToken.ThrowIfCancellationRequested();

            timings.Add(ScanTimings.ScanLevel, ScanTimings.CounterPrefix + "esi_calls", App.EsiClient.RequestCount - esiRequestsAtStart);
            timings.Add(ScanTimings.ScanLevel, ScanTimings.CounterPrefix + "zkill_requests", App.zKillClient.RequestCount - zKillRequestsAtStart);
            timings.Add(ScanTimings.ScanLevel, ScanTimings.CounterPrefix + "limiter_wait_ms", App.zKillRequestLimiter.TotalWaitMilliseconds - limiterWaitAtStart);

            for (var pilotIndex = 0; pilotIndex < pilots.Count; pilotIndex++)
            {
                if (pilots[pilotIndex].CharacterId is not { } characterId)
                    continue;

                var network = networkResults[pilotIndex];
                var preEngine = ScanActivityResolver.Resolve(
                    characterId,
                    network.Statistics,
                    network.StoredActivity,
                    null,
                    network.NewLastSuccessfulCallUtc,
                    network.PastSecondsRequested,
                    ApplicationClock.UtcNow);

                if (preEngine.Persist && preEngine.Activity is not null)
                    writes.AddActivity(preEngine.Activity);
            }

            cancellationToken.ThrowIfCancellationRequested();

            using (timings.Measure(ScanTimings.ScanLevel, "write_tx", null, "scan"))
            {
                CommitWrites(session, writes);
            }
        }
        finally
        {
            session?.Dispose();
        }

        var engineCharacterIds = pilots
            .Where(pilot => pilot.CharacterId is not null)
            .Select(pilot => pilot.CharacterId!.Value)
            .Distinct()
            .ToList();

        IReadOnlyList<PilotEngineAnalysisResult> engineResults;

        cancellationToken.ThrowIfCancellationRequested();

        using (timings.Measure(ScanTimings.ScanLevel, "engine_batch"))
        {
            engineResults = await App.RecentStyleClient.AnalyzePilotsAsync(engineCharacterIds, timings: timings);
        }

        cancellationToken.ThrowIfCancellationRequested();

        var engineByCharacter = new Dictionary<long, PilotEngineAnalysisResult>();

        for (var engineIndex = 0; engineIndex < engineCharacterIds.Count; engineIndex++)
            engineByCharacter[engineCharacterIds[engineIndex]] = engineResults[engineIndex];

        var rows = new List<PilotReportRow>();
        var postEngineWrites = new ScanWriteBatch();

        using (timings.Measure(ScanTimings.ScanLevel, "row_build"))
        {
            for (var pilotIndex = 0; pilotIndex < pilots.Count; pilotIndex++)
            {
                var pilot = pilots[pilotIndex];
                var network = networkResults[pilotIndex];

                zKillActivity? activity = null;
                var recentStyle = StyleClassification.Unknown;
                var recentIsPodder = false;
                var threatBand = "Unk";
                var statisticsCallFailed = false;
                var recentCallFailed = false;
                string? engineFailureReason = null;

                if (pilot.CharacterId is { } characterId)
                {
                    var analysisResult = engineByCharacter[characterId];

                    statisticsCallFailed = network.StatisticsFetchedThisScan && network.Statistics is null;
                    recentCallFailed = network.RecentCallFailed;

                    var killmailDerivedActivity = analysisResult.DerivedActivity?.ToActivity(characterId, ApplicationClock.UtcNow);

                    var resolution = ScanActivityResolver.Resolve(
                        characterId,
                        network.Statistics,
                        network.StoredActivity,
                        killmailDerivedActivity,
                        network.NewLastSuccessfulCallUtc,
                        network.PastSecondsRequested,
                        ApplicationClock.UtcNow);

                    activity = resolution.Activity;

                    if (resolution.Persist && activity is not null)
                        postEngineWrites.AddActivity(activity);

                    recentStyle = analysisResult.RecentStyle;
                    recentIsPodder = analysisResult.IsRecentPodder;
                    threatBand = analysisResult.ThreatBand;
                    engineFailureReason = analysisResult.FailureReason;

                    if (engineFailureReason is null)
                    {
                        if (recentCallFailed && killmailDerivedActivity?.HasPublicActivityData != true)
                        {
                            recentStyle = StyleClassification.Unknown;
                            recentIsPodder = false;
                        }

                        if (statisticsCallFailed)
                            threatBand = "Unk";
                    }
                }

                rows.Add(PilotReportRowFactory.FromPilot(
                    pilot,
                    activity,
                    network.Statistics,
                    recentStyle,
                    recentIsPodder,
                    threatBand,
                    statisticsCallFailed,
                    recentCallFailed,
                    engineFailureReason));
            }
        }

        if (timings is not null)
            timings.PilotCount = rows.Count;

        if (rows.Count == 0)
            return;

        cancellationToken.ThrowIfCancellationRequested();

        using (timings.Measure(ScanTimings.ScanLevel, "group_attach_total"))
        {
            await AttachGroupRelationshipsAsync(rows, timings);
        }

        cancellationToken.ThrowIfCancellationRequested();

        using (timings.Measure(ScanTimings.ScanLevel, "write_tx", null, "post_engine"))
        {
            CommitWrites(null, postEngineWrites);
        }

        IReadOnlySet<long> npcCorporationIds;

        using (timings.Measure(ScanTimings.ScanLevel, "npc_ids"))
        {
            npcCorporationIds = App.SdeReferenceDataStore.GetNpcCorporationIds();
        }

        using (timings.Measure(ScanTimings.ScanLevel, "annotate"))
        {
            PilotGroupCountAnnotator.Annotate(rows, npcCorporationIds);
        }

        cancellationToken.ThrowIfCancellationRequested();

        await ApplyRowsAsync(rows, context, timings);
    }

    private async Task ApplyRowsAsync(List<PilotReportRow> rows, ScanContext context, ScanTimings? timings)
    {
        using (timings.Measure(ScanTimings.ScanLevel, "grid_update"))
        {
            await Dispatcher.InvokeAsync(() =>
            {
                if (!context.IsCurrent())
                    return;

                _hoverTimer.Stop();
                _pendingHoverRow = null;
                _viewModel.Pilots.Clear();

                foreach (var row in rows)
                    _viewModel.Pilots.Add(row);
            });
        }

        if (timings is not null)
        {
            using (timings.Measure(ScanTimings.ScanLevel, "grid_render"))
            {
                await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
            }
        }
    }

    private sealed record PilotNetworkResult(
        zKillStatistics? Statistics,
        bool StatisticsFetchedThisScan,
        zKillActivity? StoredActivity,
        DateTimeOffset? NewLastSuccessfulCallUtc,
        bool RecentCallFailed,
        int? PastSecondsRequested)
    {
        public static PilotNetworkResult NoCharacter { get; } = new(null, false, null, null, false, null);

        public static PilotNetworkResult Failed { get; } = new(null, true, null, null, true, null);
    }

    private static ScanDatabaseSession? TryOpenScanSession()
    {
        try
        {
            return App.Database.OpenScanSession();
        }
        catch (Exception exception)
        {
            EngineFailureLog.Record($"scan database session could not be opened: {exception.Message}");
            return null;
        }
    }

    private static void CommitWrites(ScanDatabaseSession? session, ScanWriteBatch batch)
    {
        if (batch.IsEmpty)
            return;

        ScanDatabaseSession? ownedSession = null;

        try
        {
            ownedSession = session is null ? App.Database.OpenScanSession() : null;

            ScanWriter.Commit(
                session ?? ownedSession!,
                batch,
                App.Settings.QualificationFleetThreshold,
                ApplicationClock.UtcNow);
        }
        catch (Exception exception)
        {
            EngineFailureLog.Record($"scan write transaction failed and was rolled back: {exception.Message}");
        }
        finally
        {
            ownedSession?.Dispose();
        }
    }

    private static async Task<IReadOnlyDictionary<long, zKillStatistics>> ReadCachedStatisticsAsync(
        IReadOnlyCollection<long> characterIds,
        ScanDatabaseSession? session)
    {
        try
        {
            return await App.zKillStatisticsCache.GetManyAsync(characterIds, CacheDurations.zKillStatistics, session);
        }
        catch
        {
            return new Dictionary<long, zKillStatistics>();
        }
    }

    private static async Task<IReadOnlyDictionary<long, zKillActivity>> ReadStoredActivitiesAsync(
        IReadOnlyCollection<long> characterIds,
        ScanDatabaseSession? session)
    {
        try
        {
            return await App.zKillActivityCache.GetManyAsync(characterIds, session);
        }
        catch
        {
            return new Dictionary<long, zKillActivity>();
        }
    }

    private static async Task<PilotNetworkResult> FetchPilotNetworkAsync(
        Pilot pilot,
        int position,
        IReadOnlyDictionary<long, zKillStatistics> cachedStatistics,
        IReadOnlyDictionary<long, zKillActivity> storedActivities,
        ScanWriteBatch writes,
        ScanTimings? timings,
        CancellationToken cancellationToken)
    {
        if (pilot.CharacterId is not { } characterId)
            return PilotNetworkResult.NoCharacter;

        cancellationToken.ThrowIfCancellationRequested();

        var (statistics, statisticsFetchedThisScan) = await LoadzKillStatisticsAsync(characterId, cachedStatistics, writes, timings, cancellationToken);

        storedActivities.TryGetValue(characterId, out var storedActivity);

        (DateTimeOffset? NewLastSuccessfulCallUtc, bool Failed, int? PastSecondsRequested) recentRefresh;

        using (timings.Measure(ScanTimings.PilotLevel, "recent_refresh", characterId))
        {
            recentRefresh = await RefreshRecentKillmailsAsync(
                characterId,
                position,
                storedActivity,
                statistics,
                statisticsFetchedThisScan,
                writes,
                timings,
                cancellationToken);
        }

        return new PilotNetworkResult(
            statistics,
            statisticsFetchedThisScan,
            storedActivity,
            recentRefresh.NewLastSuccessfulCallUtc,
            recentRefresh.Failed,
            recentRefresh.PastSecondsRequested);
    }

    private static async Task AttachGroupRelationshipsAsync(List<PilotReportRow> rows, ScanTimings? timings = null)
    {
        var scannedCharacterIds = rows
            .Where(row => row.CharacterId is not null)
            .Select(row => row.CharacterId!.Value)
            .Distinct()
            .ToList();

        if (scannedCharacterIds.Count < 2)
            return;

        var groupResult = await App.RecentStyleClient.AnalyzeGroupAsync(scannedCharacterIds, timings: timings);

        using (timings.Measure(ScanTimings.EngineLevel, "relationship_assign"))
        {
            foreach (var row in rows)
            {
                if (row.CharacterId is null)
                    continue;

                row.GroupRelationships = groupResult.ForCharacter(row.CharacterId.Value).ToList();
            }
        }
    }

    private static async Task<(zKillStatistics? Statistics, bool FetchedThisScan)> LoadzKillStatisticsAsync(
        long characterId,
        IReadOnlyDictionary<long, zKillStatistics> cachedStatistics,
        ScanWriteBatch writes,
        ScanTimings? timings,
        CancellationToken cancellationToken)
    {
        using var statisticsScope = timings.Measure(ScanTimings.PilotLevel, "zkill_statistics", characterId, "cache_hit");

        if (cachedStatistics.TryGetValue(characterId, out var cached))
            return (cached, false);

        statisticsScope.Tag = "live_fetch";
        var result = await App.zKillClient.GetStatisticsAsync(characterId, cancellationToken);

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
            var style = StyleDisplayFormatter.Format(GeneralStyleClassifier.Classify(statistics).Classification);

            writes.AddStatistics(new PendingStatistics(
                characterId,
                statistics,
                style,
                statistics.NoHistory,
                ApplicationClock.UtcNow));
        }
        catch
        {
        }

        return (statistics, true);
    }

    private static async Task<(DateTimeOffset? NewLastSuccessfulCallUtc, bool Failed, int? PastSecondsRequested)> RefreshRecentKillmailsAsync(
        long characterId,
        int position,
        zKillActivity? storedActivity,
        zKillStatistics? statistics,
        bool statisticsFetchedThisScan,
        ScanWriteBatch writes,
        ScanTimings? timings,
        CancellationToken cancellationToken)
    {
        try
        {
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
            zKillRecentKillmailResult result;

            using (timings.Measure(ScanTimings.PilotLevel, "zkill_http", characterId))
            {
                result = await App.zKillClient.GetRecentKillmailsAsync(characterId, pastSeconds, cancellationToken);
            }

            switch (result.Outcome)
            {
                case zKillRecentKillmailOutcome.Success:
                    if (result.RawKillmails.Count > 0)
                    {
                        writes.AddKillmails(position, characterId, result.RawKillmails);
                        writes.AddNoHistoryClear(characterId);
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
            return (null, true, null);
        }
    }

}

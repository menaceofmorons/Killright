using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Killright.UI.Shortcuts;
using Killright.UI.Theme;
using Killright.UI.UiState;

namespace Killright.UI.MenuModal;

public partial class MenuModalWindow : Window
{
    private const double DefaultWidth = 420;
    private const double DefaultHeight = 380;
    private const double DeveloperTabWidth = 1100;
    private const double DeveloperTabHeight = 760;

    private readonly MainWindow _owner;
    private UiStateModel _workingState;
    private bool _workingSkipBackupOnClose;
    private bool _initializing = true;
    private bool _committed;

    public MenuModalWindow(MainWindow owner)
    {
        InitializeComponent();
        _owner = owner;
        _workingState = App.UiState.Current;
        _workingSkipBackupOnClose = App.SkipBackupOnClose;
        AlwaysOnTopCheckBox.IsChecked = _workingState.AlwaysOnTop;
        SkipBackupOnCloseCheckBox.IsChecked = _workingSkipBackupOnClose;
        SelectComboBoxItem(ThemeComboBox, _workingState.Theme.ToString());
        SelectComboBoxItem(FontTierComboBox, _workingState.GridFontTier.ToString());
        RefreshColumnsList();
        DeveloperTabItem.Visibility = _workingState.DeveloperTabRevealed ? Visibility.Visible : Visibility.Collapsed;
        MenuTabControl.SelectedIndex = 0;
        _initializing = false;

        Loaded += MenuModalWindow_Loaded;
    }

    private void MenuModalWindow_Loaded(object sender, RoutedEventArgs e)
    {
        MenuTabControl.SelectedIndex = 0;
    }

    private void AlwaysOnTop_Changed(object sender, RoutedEventArgs e)
    {
        _workingState = _workingState with { AlwaysOnTop = AlwaysOnTopCheckBox.IsChecked == true };
        _owner.Topmost = _workingState.AlwaysOnTop;
    }

    private void Theme_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_initializing || ThemeComboBox.SelectedItem is not ComboBoxItem item)
            return;

        var theme = Enum.Parse<AppTheme>((string)item.Tag);
        _workingState = _workingState with { Theme = theme };
        AppearanceManager.ApplyTheme(theme);
    }

    private void FontTier_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_initializing || FontTierComboBox.SelectedItem is not ComboBoxItem item)
            return;

        var tier = Enum.Parse<GridFontTier>((string)item.Tag);
        _workingState = _workingState with { GridFontTier = tier };
        AppearanceManager.ApplyFontTier(tier);
    }

    private void ColumnVisibility_Changed(object sender, RoutedEventArgs e)
    {
        if (_initializing || sender is not CheckBox { DataContext: ColumnRow row })
            return;

        if (row.Id == ColumnIds.Pilot)
            return;

        var isVisible = ((CheckBox)sender).IsChecked == true;

        _workingState = _workingState with
        {
            Columns = _workingState.Columns
                .Select(column => column.Id == row.Id ? column with { Visible = isVisible } : column)
                .ToList()
        };

        _owner.ApplyPreviewColumns(_workingState);
    }

    private ColumnRow? _draggedRow;
    private Point _dragStartPoint;

    private void DragHandle_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStartPoint = e.GetPosition(null);
        _draggedRow = ((FrameworkElement)sender).DataContext as ColumnRow;
    }

    private void DragHandle_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_draggedRow is null || e.LeftButton != MouseButtonState.Pressed)
            return;

        var current = e.GetPosition(null);

        if (Math.Abs(current.X - _dragStartPoint.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(current.Y - _dragStartPoint.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;

        var row = _draggedRow;
        _draggedRow = null;
        DragDrop.DoDragDrop((DependencyObject)sender, row, DragDropEffects.Move);
    }

    private void ColumnsListBox_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(typeof(ColumnRow)) ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    private void ColumnsListBox_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(typeof(ColumnRow)) is not ColumnRow draggedRow)
            return;

        var targetRow = FindRowUnderMouse(e.GetPosition(ColumnsListBox));

        if (targetRow is null || targetRow.Id == draggedRow.Id)
            return;

        var ordered = _workingState.Columns.OrderBy(column => column.DisplayIndex).ToList();
        var fromIndex = ordered.FindIndex(column => column.Id == draggedRow.Id);
        var toIndex = ordered.FindIndex(column => column.Id == targetRow.Id);

        if (fromIndex < 0 || toIndex < 0)
            return;

        var moved = ordered[fromIndex];
        ordered.RemoveAt(fromIndex);
        ordered.Insert(toIndex, moved);

        _workingState = _workingState with
        {
            Columns = ordered.Select((column, position) => column with { DisplayIndex = position }).ToList()
        };

        _owner.ApplyPreviewColumns(_workingState);
        RefreshColumnsList();
    }

    private ColumnRow? FindRowUnderMouse(Point position)
    {
        if (FindAncestorOrSelf<ListBoxItem>(ColumnsListBox.InputHitTest(position) as DependencyObject) is not { } item)
            return null;

        return item.DataContext as ColumnRow;
    }

    private static T? FindAncestorOrSelf<T>(DependencyObject? current) where T : DependencyObject
    {
        while (current is not null)
        {
            if (current is T match)
                return match;

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }

    private void RefreshColumnsList()
    {
        var catalogById = UiStateDefaults.ColumnCatalog.ToDictionary(catalogEntry => catalogEntry.Id);

        ColumnsListBox.ItemsSource = _workingState.Columns
            .Where(column => catalogById.TryGetValue(column.Id, out var catalogEntry)
                && (!catalogEntry.RequiresDeveloperMode || _workingState.DeveloperTabRevealed))
            .OrderBy(column => column.DisplayIndex)
            .Select(column => new ColumnRow
            {
                Id = column.Id,
                Label = catalogById[column.Id].Label,
                IsVisible = column.Visible,
                CanHide = column.Id != ColumnIds.Pilot
            })
            .ToList();
    }

    private sealed class ColumnRow
    {
        public string Id { get; init; } = string.Empty;
        public string Label { get; init; } = string.Empty;
        public bool IsVisible { get; init; }
        public bool CanHide { get; init; }
    }

    private static void SelectComboBoxItem(ComboBox comboBox, string tag)
    {
        foreach (var obj in comboBox.Items)
        {
            if (obj is ComboBoxItem item && (string)item.Tag == tag)
            {
                comboBox.SelectedItem = item;
                return;
            }
        }
    }

    private void SkipBackupOnClose_Changed(object sender, RoutedEventArgs e)
    {
        _workingSkipBackupOnClose = SkipBackupOnCloseCheckBox.IsChecked == true;

        if (_initializing || !_workingSkipBackupOnClose)
            return;

        MessageBox.Show(
            "Skip Backup on Close is a one-off setting for this session only. Backup will run normally the next time KillRight starts.",
            "KillRight",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }

    private void ResetToDefaults_Click(object sender, RoutedEventArgs e)
    {
        var workArea = SystemParameters.WorkArea;
        var (left, top) = WindowBoundsCalculator.CenterOn(
            workArea.Left,
            workArea.Top,
            workArea.Width,
            workArea.Height,
            UiStateDefaults.WindowWidth,
            UiStateDefaults.WindowHeight);

        _workingState = _workingState with
        {
            WindowLeft = left,
            WindowTop = top,
            WindowWidth = UiStateDefaults.WindowWidth,
            WindowHeight = UiStateDefaults.WindowHeight,
            AlwaysOnTop = UiStateDefaults.AlwaysOnTop,
            Theme = UiStateDefaults.Theme,
            GridFontTier = UiStateDefaults.DefaultGridFontTier,
            Columns = UiStateDefaults.DefaultColumns,
            DeveloperTabRevealed = UiStateDefaults.DeveloperTabRevealed
        };

        AlwaysOnTopCheckBox.IsChecked = _workingState.AlwaysOnTop;
        SelectComboBoxItem(ThemeComboBox, _workingState.Theme.ToString());
        SelectComboBoxItem(FontTierComboBox, _workingState.GridFontTier.ToString());
        RefreshColumnsList();
        DeveloperTabItem.Visibility = Visibility.Collapsed;
        if (MenuTabControl.SelectedItem == DeveloperTabItem)
            MenuTabControl.SelectedIndex = 0;
        _owner.ApplyPreviewBounds(_workingState);
        _owner.ApplyPreviewColumns(_workingState);
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        _committed = true;
        App.UiState.Commit(_workingState);
        App.SkipBackupOnClose = _workingSkipBackupOnClose;
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void ToggleDeveloperTab()
    {
        var revealing = !_workingState.DeveloperTabRevealed;
        _workingState = _workingState with { DeveloperTabRevealed = revealing };
        DeveloperTabItem.Visibility = revealing ? Visibility.Visible : Visibility.Collapsed;
        RefreshColumnsList();
        _owner.ApplyPreviewColumns(_workingState);

        if (revealing)
        {
            MessageBox.Show(
                "The Developer tab exposes internal diagnostics and cache-clearing tools not intended for general use.",
                "KillRight",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        else if (MenuTabControl.SelectedItem == DeveloperTabItem)
        {
            MenuTabControl.SelectedIndex = 0;
        }
    }

    private void MenuTabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_initializing)
            return;

        var selectedDeveloperTab = MenuTabControl.SelectedItem == DeveloperTabItem;

        if (selectedDeveloperTab)
        {
            Width = DeveloperTabWidth;
            Height = DeveloperTabHeight;
        }
        else
        {
            Width = DefaultWidth;
            Height = DefaultHeight;
        }

        UpdateLayout();

        if (selectedDeveloperTab)
            ForceCompositorRepaint();
    }

    private void ForceCompositorRepaint()
    {
        var previousState = WindowState;
        WindowState = WindowState.Minimized;
        WindowState = previousState;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);

        if (!_committed)
        {
            _owner.ApplyPreviewBounds(App.UiState.Current);
            AppearanceManager.ApplyTheme(App.UiState.Current.Theme);
            AppearanceManager.ApplyFontTier(App.UiState.Current.GridFontTier);
            _owner.ApplyPreviewColumns(App.UiState.Current);
        }
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.Q)
        {
            e.Handled = true;
            Close();
            _owner.Close();
        }
        else if (Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && e.Key == Key.D)
        {
            e.Handled = true;
            ToggleDeveloperTab();
        }
        else if (e.Key == Key.F1)
        {
            e.Handled = true;
            new ShortcutsWindow { Owner = this }.ShowDialog();
        }
    }
}

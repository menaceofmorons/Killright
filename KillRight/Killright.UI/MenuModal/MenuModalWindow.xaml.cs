using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using Killright.UI.Shortcuts;
using Killright.UI.UiState;

namespace Killright.UI.MenuModal;

public partial class MenuModalWindow : Window
{
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
        _initializing = false;
    }

    private void AlwaysOnTop_Changed(object sender, RoutedEventArgs e)
    {
        _workingState = _workingState with { AlwaysOnTop = AlwaysOnTopCheckBox.IsChecked == true };
        _owner.Topmost = _workingState.AlwaysOnTop;
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
            AlwaysOnTop = UiStateDefaults.AlwaysOnTop
        };

        AlwaysOnTopCheckBox.IsChecked = _workingState.AlwaysOnTop;
        _owner.ApplyPreviewBounds(_workingState);
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

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);

        if (!_committed)
            _owner.ApplyPreviewBounds(App.UiState.Current);
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.Q)
        {
            e.Handled = true;
            Close();
            _owner.Close();
        }
        else if (e.Key == Key.F1)
        {
            e.Handled = true;
            new ShortcutsWindow { Owner = this }.ShowDialog();
        }
    }
}

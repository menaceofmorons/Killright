using System.Windows;
using System.Windows.Threading;

namespace Killright.UI.InfoSheet;

public partial class InfoSheetWindow : Window
{
    private bool _closeScheduled;
    private bool _dismissArmed;

    public InfoSheetWindow(InfoSheetViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        ContentRendered += InfoSheetWindow_ContentRendered;
    }

    private void InfoSheetWindow_ContentRendered(object? sender, EventArgs e)
    {
        ContentRendered -= InfoSheetWindow_ContentRendered;

        Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () =>
        {
            _dismissArmed = true;

            if (IsVisible && !IsActive)
                Activate();
        });
    }

    private void InfoSheetWindow_Deactivated(object? sender, EventArgs e)
    {
        if (!_dismissArmed || _closeScheduled)
            return;

        _closeScheduled = true;

        // Closing synchronously here can re-enter WPF's Show()/activation
        // pump when the owner is Topmost (MainWindow defaults to
        // AlwaysOnTop) and the OS immediately reasserts it above this
        // non-topmost popup, firing Deactivated before Show() has finished
        // -- deferring to the next dispatcher cycle avoids that crash.
        Dispatcher.BeginInvoke(DispatcherPriority.Background, Close);
    }
}

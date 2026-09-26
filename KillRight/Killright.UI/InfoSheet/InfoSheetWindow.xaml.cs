using System.Windows;
using System.Windows.Threading;
using Killright.UI.ViewModels;

namespace Killright.UI.InfoSheet;

public partial class InfoSheetWindow : Window
{
    private bool _closeScheduled;

    public InfoSheetWindow(PilotReportRow row, bool developerModeRevealed)
    {
        InitializeComponent();
        DataContext = new InfoSheetViewModel(row, developerModeRevealed);
    }

    private void InfoSheetWindow_Deactivated(object? sender, EventArgs e)
    {
        if (_closeScheduled)
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

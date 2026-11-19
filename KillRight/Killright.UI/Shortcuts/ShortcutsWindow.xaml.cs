using System.Windows;
using Killright.UI.Resources;

namespace Killright.UI.Shortcuts;

public partial class ShortcutsWindow : Window
{
    public ShortcutsWindow()
    {
        DataContext = F1HelpContent.Create(App.Settings, AppVersion.Read(typeof(App).Assembly));
        InitializeComponent();
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        var error = F1SizeRecorder.Record(ActualWidth, ActualHeight);

        if (error is not null)
        {
            MessageBox.Show(
                this,
                string.Format(UiText.ShortcutsSizingWriteFailed, error),
                UiText.WindowTitleShortcuts,
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}

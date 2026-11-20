using System.Windows;

namespace Killright.UI.Shortcuts;

public partial class ShortcutsWindow : Window
{
    public ShortcutsWindow()
    {
        DataContext = F1HelpContent.Create(App.Settings, AppVersion.Read(typeof(App).Assembly));
        InitializeComponent();
        Width = App.Settings.Window.F1.Width;
        Height = App.Settings.Window.F1.Height;
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}

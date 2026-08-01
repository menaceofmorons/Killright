using System.Collections.ObjectModel;

namespace Killright.UI.ViewModels;

public class MainWindowViewModel
{
    public ObservableCollection<PilotReportRow> Pilots { get; } = new();
}
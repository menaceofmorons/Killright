using System.Collections.ObjectModel;

namespace PilotIntel.UI.ViewModels;

public class MainWindowViewModel
{
    public ObservableCollection<PilotReportRow> Pilots { get; } = new();
}
namespace Killright.UI.ViewModels;

public class MainWindowViewModel
{
    public ReplaceableObservableCollection<PilotReportRow> Pilots { get; } = new();
}

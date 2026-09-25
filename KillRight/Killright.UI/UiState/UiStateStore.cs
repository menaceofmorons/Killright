using System.Windows.Threading;

namespace Killright.UI.UiState;

public sealed class UiStateStore
{
    private readonly string _statePath;
    private readonly DispatcherTimer _debounceTimer;
    private UiStateModel _current;

    public UiStateStore(string? statePath = null)
    {
        _statePath = statePath ?? UiStateLoader.GetDefaultStatePath();

        var result = UiStateLoader.LoadOrDefault(_statePath);
        _current = result.State;
        WasCorruptOnLoad = result.WasCorrupt;
        IsUsingDefaults = !result.FileExisted || result.WasCorrupt;

        _debounceTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(500)
        };
        _debounceTimer.Tick += (_, _) =>
        {
            _debounceTimer.Stop();
            UiStateLoader.Save(_current, _statePath);
        };
    }

    public bool WasCorruptOnLoad { get; }

    public bool IsUsingDefaults { get; }

    public UiStateModel Current => _current;

    public void UpdateBounds(double left, double top, double width, double height)
    {
        _current = _current with { WindowLeft = left, WindowTop = top, WindowWidth = width, WindowHeight = height };
        _debounceTimer.Stop();
        _debounceTimer.Start();
    }

    public void Commit(UiStateModel state)
    {
        _current = state;
        SaveNow();
    }

    public void SaveNow()
    {
        _debounceTimer.Stop();
        UiStateLoader.Save(_current, _statePath);
    }
}

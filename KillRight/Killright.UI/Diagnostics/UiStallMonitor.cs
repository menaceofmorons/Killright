using System.Diagnostics;
using System.Windows.Threading;

namespace Killright.UI.Diagnostics;

internal sealed class UiStallMonitor
{
    public const int IntervalMilliseconds = 15;

    private readonly DispatcherTimer _timer;
    private long _lastTickTimestamp;

    private UiStallMonitor(Dispatcher dispatcher)
    {
        _timer = new DispatcherTimer(DispatcherPriority.Normal, dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(IntervalMilliseconds)
        };
        _timer.Tick += OnTick;
    }

    public double MaxStallMilliseconds { get; private set; }

    public static UiStallMonitor Start(Dispatcher dispatcher)
    {
        var monitor = new UiStallMonitor(dispatcher);
        monitor._lastTickTimestamp = Stopwatch.GetTimestamp();
        monitor._timer.Start();
        return monitor;
    }

    public double Stop()
    {
        _timer.Stop();
        _timer.Tick -= OnTick;
        var gap = Stopwatch.GetElapsedTime(_lastTickTimestamp).TotalMilliseconds;
        return Math.Max(MaxStallMilliseconds, gap);
    }

    private void OnTick(object? sender, EventArgs e)
    {
        var gap = Stopwatch.GetElapsedTime(_lastTickTimestamp).TotalMilliseconds;
        _lastTickTimestamp = Stopwatch.GetTimestamp();

        if (gap > MaxStallMilliseconds)
            MaxStallMilliseconds = gap;
    }
}

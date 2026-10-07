using System.Diagnostics;

namespace Killright.UI.Scan;

public sealed class IdleCheckpointScheduler
{
    public const string IdleTag = "idle";

    private readonly TimeSpan _delay;
    private readonly Func<bool> _hasPendingWal;
    private readonly Action _checkpoint;
    private readonly Func<TimeSpan, Task> _delayAsync;
    private readonly Action<string>? _logFailure;
    private readonly Action<string, double>? _recordCheckpoint;
    private readonly object _gate = new();
    private int _activeCount;
    private long _generation;
    private bool _checkpointRunning;

    public IdleCheckpointScheduler(
        TimeSpan delay,
        Func<bool> hasPendingWal,
        Action checkpoint,
        Func<TimeSpan, Task>? delayAsync = null,
        Action<string>? logFailure = null,
        Action<string, double>? recordCheckpoint = null)
    {
        _delay = delay;
        _hasPendingWal = hasPendingWal;
        _checkpoint = checkpoint;
        _delayAsync = delayAsync ?? Task.Delay;
        _logFailure = logFailure;
        _recordCheckpoint = recordCheckpoint;
    }

    public void ActivityStarted()
    {
        lock (_gate)
        {
            _activeCount++;
            _generation++;
        }
    }

    public Task<bool> ActivityFinished()
    {
        long generation;

        lock (_gate)
        {
            if (_activeCount > 0)
                _activeCount--;

            generation = _generation;
        }

        return Task.Run(() => RunAfterDelayAsync(generation));
    }

    private async Task<bool> RunAfterDelayAsync(long generation)
    {
        try
        {
            await _delayAsync(_delay);

            lock (_gate)
            {
                if (_generation != generation || _activeCount > 0 || _checkpointRunning)
                    return false;

                _checkpointRunning = true;
            }

            try
            {
                if (!_hasPendingWal())
                    return false;

                var startTimestamp = Stopwatch.GetTimestamp();
                _checkpoint();
                RecordCheckpoint(Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds);

                return true;
            }
            finally
            {
                lock (_gate)
                    _checkpointRunning = false;
            }
        }
        catch (Exception exception)
        {
            try
            {
                _logFailure?.Invoke($"Idle checkpoint failed: {exception.Message}");
            }
            catch
            {
            }

            return false;
        }
    }

    private void RecordCheckpoint(double milliseconds)
    {
        try
        {
            _recordCheckpoint?.Invoke(IdleTag, milliseconds);
        }
        catch
        {
        }
    }
}

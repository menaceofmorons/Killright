using Killright.Storage.Diagnostics;

namespace Killright.UI.Scan;

public sealed record ScanContext(long Id, CancellationToken Token, Func<bool> IsCurrent);

public sealed class ScanCoordinator
{
    private readonly Func<IReadOnlyList<string>, ScanTimings?, ScanContext, Task> _runScan;
    private readonly Action _scanStarted;
    private readonly Action _scanFinished;
    private readonly Action<Exception>? _logFailure;
    private readonly object _gate = new();
    private CancellationTokenSource? _current;
    private long _currentId;
    private Task _tail = Task.CompletedTask;

    public ScanCoordinator(
        Func<IReadOnlyList<string>, ScanTimings?, ScanContext, Task> runScan,
        Action scanStarted,
        Action scanFinished,
        Action<Exception>? logFailure = null)
    {
        _runScan = runScan;
        _scanStarted = scanStarted;
        _scanFinished = scanFinished;
        _logFailure = logFailure;
    }

    public Task Submit(IReadOnlyList<string> pilotNames, ScanTimings? timings = null)
    {
        CancellationTokenSource source;
        ScanContext context;
        Task previous;

        lock (_gate)
        {
            _current?.Cancel();
            source = new CancellationTokenSource();
            _current = source;
            var id = ++_currentId;
            context = new ScanContext(id, source.Token, () => IsCurrent(id));
            previous = _tail;
            _scanStarted();
            _tail = Task.Run(() => RunAsync(pilotNames, timings, context, source, previous));
            return _tail;
        }
    }

    public void CancelCurrent()
    {
        lock (_gate)
            _current?.Cancel();
    }

    public bool WaitForIdle(TimeSpan timeout)
    {
        Task tail;

        lock (_gate)
            tail = _tail;

        try
        {
            return tail.Wait(timeout);
        }
        catch
        {
            return true;
        }
    }

    private bool IsCurrent(long id)
    {
        lock (_gate)
            return _currentId == id && _current is { IsCancellationRequested: false };
    }

    private async Task RunAsync(
        IReadOnlyList<string> pilotNames,
        ScanTimings? timings,
        ScanContext context,
        CancellationTokenSource source,
        Task previous)
    {
        try
        {
            await previous;

            if (context.Token.IsCancellationRequested)
                return;

            await _runScan(pilotNames, timings, context);
        }
        catch (OperationCanceledException) when (context.Token.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            try
            {
                _logFailure?.Invoke(exception);
            }
            catch
            {
            }
        }
        finally
        {
            lock (_gate)
            {
                if (ReferenceEquals(_current, source))
                    _current = null;
            }

            source.Dispose();

            try
            {
                _scanFinished();
            }
            catch
            {
            }
        }
    }
}

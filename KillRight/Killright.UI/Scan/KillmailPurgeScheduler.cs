using System.Diagnostics;
using Killright.Storage.Killmails;

namespace Killright.UI.Scan;

public sealed class KillmailPurgeScheduler
{
    public const string StartupTag = "startup";
    public const string PostScanTag = "post_scan";
    public static readonly TimeSpan PostScanInterval = TimeSpan.FromHours(4);

    private readonly IRecentKillmailCache _cache;
    private readonly Func<DateTimeOffset> _utcNow;
    private readonly Action<string>? _logFailure;
    private readonly Action<string, double>? _recordPass;
    private readonly object _gate = new();
    private int _runningScans;
    private bool _passRunning;
    private DateTimeOffset? _lastPassUtc;

    public KillmailPurgeScheduler(
        IRecentKillmailCache cache,
        Func<DateTimeOffset>? utcNow = null,
        Action<string>? logFailure = null,
        Action<string, double>? recordPass = null)
    {
        _cache = cache;
        _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
        _logFailure = logFailure;
        _recordPass = recordPass;
    }

    public void ScanStarted()
    {
        lock (_gate)
            _runningScans++;
    }

    public void ScanFinished()
    {
        lock (_gate)
        {
            if (_runningScans > 0)
                _runningScans--;
        }
    }

    public Task<bool> RunStartupPassAsync() => TryRunPassAsync(StartupTag, requireInterval: false);

    public Task<bool> RunPostScanPassAsync() => TryRunPassAsync(PostScanTag, requireInterval: true);

    private async Task<bool> TryRunPassAsync(string tag, bool requireInterval)
    {
        lock (_gate)
        {
            if (_runningScans > 0 || _passRunning)
                return false;

            if (requireInterval && _lastPassUtc is { } lastPassUtc && _utcNow() - lastPassUtc <= PostScanInterval)
                return false;

            _passRunning = true;
        }

        var startTimestamp = Stopwatch.GetTimestamp();

        try
        {
            await Task.Run(() => _cache.RemoveExpiredAsync());

            lock (_gate)
                _lastPassUtc = _utcNow();

            RecordPass(tag, Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds);
            return true;
        }
        catch (Exception exception)
        {
            try
            {
                _logFailure?.Invoke($"Killmail purge pass failed ({tag}): {exception.Message}");
            }
            catch
            {
            }

            return false;
        }
        finally
        {
            lock (_gate)
                _passRunning = false;
        }
    }

    private void RecordPass(string tag, double milliseconds)
    {
        try
        {
            _recordPass?.Invoke(tag, milliseconds);
        }
        catch
        {
        }
    }
}

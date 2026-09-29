using System.Diagnostics;

namespace Killright.Storage.Diagnostics;

public sealed record TimingRow(
    DateTimeOffset UtcTimestamp,
    string ScanId,
    int PilotCount,
    string Level,
    long? CharacterId,
    string Phase,
    double Milliseconds,
    string? Tag);

public sealed class TimingScope : IDisposable
{
    public static readonly TimingScope None = new(null, string.Empty, string.Empty, null, null);

    private readonly ScanTimings? _owner;
    private readonly string _level;
    private readonly string _phase;
    private long? _characterId;
    private readonly long _startTimestamp;
    private string? _tag;
    private bool _disposed;

    internal TimingScope(ScanTimings? owner, string level, string phase, long? characterId, string? tag)
    {
        _owner = owner;
        _level = level;
        _phase = phase;
        _characterId = characterId;
        _tag = tag;
        _startTimestamp = Stopwatch.GetTimestamp();
    }

    public long? CharacterId
    {
        get => _characterId;
        set
        {
            if (_owner is not null)
                _characterId = value;
        }
    }

    public string? Tag
    {
        get => _tag;
        set
        {
            if (_owner is not null)
                _tag = value;
        }
    }

    public void Dispose()
    {
        if (_owner is null || _disposed)
            return;

        _disposed = true;
        _owner.Record(_level, _phase, Stopwatch.GetElapsedTime(_startTimestamp).TotalMilliseconds, _characterId, _tag);
    }
}

public sealed class ScanTimings
{
    public const string ScanLevel = "scan";
    public const string PilotLevel = "pilot";
    public const string EngineLevel = "engine";
    public const string CounterPrefix = "count_";

    private readonly object _gate = new();
    private readonly List<TimingRow> _rows = new();
    private readonly Func<DateTimeOffset> _utcNow;

    public ScanTimings(Func<DateTimeOffset>? utcNow = null)
    {
        _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
        ScanId = Guid.NewGuid().ToString("N")[..8];
    }

    private readonly long _createdTimestamp = Stopwatch.GetTimestamp();

    public string ScanId { get; }

    public double ElapsedMilliseconds => Stopwatch.GetElapsedTime(_createdTimestamp).TotalMilliseconds;

    public int PilotCount { get; set; }

    public IReadOnlyList<TimingRow> Rows
    {
        get
        {
            lock (_gate)
                return _rows.ToList();
        }
    }

    public TimingScope StartScope(string level, string phase, long? characterId = null, string? tag = null)
        => new(this, level, phase, characterId, tag);

    public void Record(string level, string phase, double milliseconds, long? characterId = null, string? tag = null)
    {
        var row = new TimingRow(_utcNow(), ScanId, PilotCount, level, characterId, phase, milliseconds, tag);

        lock (_gate)
            _rows.Add(row);
    }

    public void RecordEngine(
        long? characterId,
        IReadOnlyDictionary<string, double>? timingsMs,
        IReadOnlyDictionary<string, long>? timingCounts)
    {
        if (timingsMs is not null)
        {
            foreach (var pair in timingsMs)
                Record(EngineLevel, pair.Key, pair.Value, characterId);
        }

        if (timingCounts is not null)
        {
            foreach (var pair in timingCounts)
                Record(EngineLevel, CounterPrefix + pair.Key, pair.Value, characterId);
        }
    }

    public void Flush(string? path = null)
    {
        IReadOnlyList<TimingRow> rows;

        lock (_gate)
            rows = _rows.Select(row => row with { PilotCount = PilotCount }).ToList();

        ScanTimingLog.Append(path ?? ScanTimingLogPaths.GetDefaultLogPath(), rows);
    }
}

public static class ScanTimingsExtensions
{
    public static TimingScope Measure(
        this ScanTimings? timings,
        string level,
        string phase,
        long? characterId = null,
        string? tag = null)
        => timings is null ? TimingScope.None : timings.StartScope(level, phase, characterId, tag);

    public static void AddEngine(
        this ScanTimings? timings,
        long? characterId,
        IReadOnlyDictionary<string, double>? timingsMs,
        IReadOnlyDictionary<string, long>? timingCounts)
    {
        timings?.RecordEngine(characterId, timingsMs, timingCounts);
    }

    public static void Add(
        this ScanTimings? timings,
        string level,
        string phase,
        double milliseconds,
        long? characterId = null,
        string? tag = null)
    {
        timings?.Record(level, phase, milliseconds, characterId, tag);
    }
}

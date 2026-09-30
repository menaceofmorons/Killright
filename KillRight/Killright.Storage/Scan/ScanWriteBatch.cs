using Killright.Integration.zKill;
using Killright.Shared.Killmails;
using Killright.Shared.zKill;
using Killright.Storage.Identity;

namespace Killright.Storage.Scan;

public sealed record PendingStatistics(
    long CharacterId,
    zKillStatistics Statistics,
    string GeneralStyle,
    bool NoHistory,
    DateTimeOffset CheckedAtUtc);

public sealed record PendingKillmails(
    int Position,
    long CharacterId,
    IReadOnlyList<RawKillmail> Killmails);

public sealed class ScanWriteBatch
{
    private readonly object _gate = new();
    private readonly List<PilotIdentityCacheRecord> _identities = new();
    private readonly List<EsiEntityName> _entityNames = new();
    private readonly List<PendingStatistics> _statistics = new();
    private readonly List<PendingKillmails> _killmails = new();
    private readonly List<long> _noHistoryClears = new();
    private readonly List<zKillActivity> _activities = new();

    public bool IsEmpty
    {
        get
        {
            lock (_gate)
            {
                return _identities.Count == 0
                    && _entityNames.Count == 0
                    && _statistics.Count == 0
                    && _killmails.Count == 0
                    && _noHistoryClears.Count == 0
                    && _activities.Count == 0;
            }
        }
    }

    public void AddIdentity(PilotIdentityCacheRecord record)
    {
        lock (_gate)
            _identities.Add(record);
    }

    public void AddEntityNames(IEnumerable<EsiEntityName> names)
    {
        lock (_gate)
            _entityNames.AddRange(names);
    }

    public void AddStatistics(PendingStatistics statistics)
    {
        lock (_gate)
            _statistics.Add(statistics);
    }

    public void AddKillmails(int position, long characterId, IReadOnlyList<RawKillmail> killmails)
    {
        lock (_gate)
            _killmails.Add(new PendingKillmails(position, characterId, killmails));
    }

    public void AddNoHistoryClear(long characterId)
    {
        lock (_gate)
            _noHistoryClears.Add(characterId);
    }

    public void AddActivity(zKillActivity activity)
    {
        lock (_gate)
            _activities.Add(activity);
    }

    public IReadOnlyList<PilotIdentityCacheRecord> Identities
    {
        get
        {
            lock (_gate)
            {
                return _identities
                    .GroupBy(record => record.InputName)
                    .Select(group => group.Last())
                    .ToList();
            }
        }
    }

    public IReadOnlyList<EsiEntityName> EntityNames
    {
        get
        {
            lock (_gate)
            {
                return _entityNames
                    .GroupBy(name => name.EntityId)
                    .Select(group => group.Last())
                    .ToList();
            }
        }
    }

    public IReadOnlyList<PendingStatistics> Statistics
    {
        get
        {
            lock (_gate)
            {
                return _statistics
                    .GroupBy(statistics => statistics.CharacterId)
                    .Select(group => group.Last())
                    .ToList();
            }
        }
    }

    public IReadOnlyList<PendingKillmails> Killmails
    {
        get
        {
            lock (_gate)
                return _killmails.OrderBy(pending => pending.Position).ToList();
        }
    }

    public IReadOnlyList<long> NoHistoryClears
    {
        get
        {
            lock (_gate)
                return _noHistoryClears.Distinct().ToList();
        }
    }

    public IReadOnlyList<zKillActivity> Activities
    {
        get
        {
            lock (_gate)
            {
                return _activities
                    .GroupBy(activity => activity.CharacterId)
                    .Select(group => group.Last())
                    .ToList();
            }
        }
    }
}

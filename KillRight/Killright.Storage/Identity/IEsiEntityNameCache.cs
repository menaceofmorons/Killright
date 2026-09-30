using Killright.Storage.Database;

namespace Killright.Storage.Identity;

public static class EsiEntityTypes
{
    public const string Corporation = "C";
    public const string Alliance = "A";
}

public sealed record EsiEntityName(long EntityId, string EntityType, string Name);

public interface IEsiEntityNameCache
{
    Task<IReadOnlyDictionary<long, string>> GetNamesAsync(IReadOnlyCollection<long> entityIds, CancellationToken cancellationToken = default);
    Task UpsertAsync(IReadOnlyCollection<EsiEntityName> names, CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<long, string>> GetNamesAsync(
        IReadOnlyCollection<long> entityIds,
        ScanDatabaseSession? session,
        CancellationToken cancellationToken = default)
    {
        return GetNamesAsync(entityIds, cancellationToken);
    }
}

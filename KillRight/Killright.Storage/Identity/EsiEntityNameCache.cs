using Microsoft.Data.Sqlite;
using Killright.Shared.Data;
using Killright.Storage.Database;

namespace Killright.Storage.Identity;

public sealed class EsiEntityNameCache : IEsiEntityNameCache
{
    private readonly KillRightDatabase _database;

    public EsiEntityNameCache(KillRightDatabase database)
    {
        _database = database;
    }

    public Task<IReadOnlyDictionary<long, string>> GetNamesAsync(
        IReadOnlyCollection<long> entityIds,
        CancellationToken cancellationToken = default)
    {
        return GetNamesAsync(entityIds, null, cancellationToken);
    }

    public Task<IReadOnlyDictionary<long, string>> GetNamesAsync(
        IReadOnlyCollection<long> entityIds,
        ScanDatabaseSession? session,
        CancellationToken cancellationToken = default)
    {
        var names = new Dictionary<long, string>();

        if (entityIds.Count == 0)
            return Task.FromResult<IReadOnlyDictionary<long, string>>(names);

        using var ownedConnection = session is null ? _database.OpenConnection() : null;
        var connection = session?.Connection ?? ownedConnection!;

        using var command = connection.CreateCommand();
        command.CommandText = $"""
                              SELECT entity_id, name
                              FROM esi_entity_name_cache
                              WHERE entity_id IN ({string.Join(", ", entityIds.Distinct())});
                              """;

        using var reader = command.ExecuteReader();

        while (reader.Read())
            names[reader.GetInt64(0)] = reader.GetString(1);

        return Task.FromResult<IReadOnlyDictionary<long, string>>(names);
    }

    public Task UpsertAsync(
        IReadOnlyCollection<EsiEntityName> names,
        CancellationToken cancellationToken = default)
    {
        if (names.Count == 0)
            return Task.CompletedTask;

        var distinctNames = names
            .GroupBy(name => name.EntityId)
            .Select(group => group.Last())
            .ToList();

        using var scope = _database.BeginWrite();

        using (var deleteCommand = scope.Connection.CreateCommand())
        {
            deleteCommand.Transaction = scope.Transaction;
            deleteCommand.CommandText = $"DELETE FROM esi_entity_name_cache WHERE entity_id IN ({string.Join(", ", distinctNames.Select(name => name.EntityId))});";
            deleteCommand.ExecuteNonQuery();
        }

        using (var insertCommand = scope.Connection.CreateCommand())
        {
            insertCommand.Transaction = scope.Transaction;
            insertCommand.CommandText = $"""
                                        INSERT INTO esi_entity_name_cache (entity_id, entity_type, name)
                                        VALUES {string.Join(", ", distinctNames.Select(name => $"({name.EntityId}, {SqlValueFormatter.String(name.EntityType)}, {SqlValueFormatter.String(name.Name)})"))};
                                        """;
            insertCommand.ExecuteNonQuery();
        }

        scope.Commit();

        return Task.CompletedTask;
    }
}

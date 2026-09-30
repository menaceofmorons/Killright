using DuckDB.NET.Data;
using Killright.Shared.Data;
using Killright.Storage.Database;

namespace Killright.Storage.Identity;

public sealed class DuckDbEsiEntityNameCache : IEsiEntityNameCache
{
    private readonly KillRightDatabase _database;

    public DuckDbEsiEntityNameCache(KillRightDatabase database)
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

        using var connection = new DuckDBConnection(_database.ConnectionString);
        connection.Open();

        using var transaction = connection.BeginTransaction();

        using (var deleteCommand = connection.CreateCommand())
        {
            deleteCommand.Transaction = transaction;
            deleteCommand.CommandText = $"DELETE FROM esi_entity_name_cache WHERE entity_id IN ({string.Join(", ", distinctNames.Select(name => name.EntityId))});";
            deleteCommand.ExecuteNonQuery();
        }

        using (var insertCommand = connection.CreateCommand())
        {
            insertCommand.Transaction = transaction;
            insertCommand.CommandText = $"""
                                        INSERT INTO esi_entity_name_cache (entity_id, entity_type, name)
                                        VALUES {string.Join(", ", distinctNames.Select(name => $"({name.EntityId}, {SqlValueFormatter.String(name.EntityType)}, {SqlValueFormatter.String(name.Name)})"))};
                                        """;
            insertCommand.ExecuteNonQuery();
        }

        transaction.Commit();

        return Task.CompletedTask;
    }
}

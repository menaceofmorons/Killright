using System.Collections.Concurrent;
using DuckDB.NET.Data;
using Killright.Shared.Data;
using Killright.Shared.Sde;
using Killright.Storage.Database;
using Killright.Storage.Diagnostics;

namespace Killright.Storage.Sde;

public sealed class DuckDbSdeReferenceDataStore : ISdeReferenceDataStore
{
    private const int InsertBatchSize = 2000;

    private readonly KillRightDatabase _database;
    private readonly ConcurrentDictionary<long, string> _typeNames = new();
    private readonly ConcurrentDictionary<long, string> _solarSystemNames = new();
    private IReadOnlyDictionary<long, string>? _factionNames;
    private IReadOnlySet<long>? _npcCorporationIds;

    public DuckDbSdeReferenceDataStore(KillRightDatabase database)
    {
        _database = database;
    }

    public string? GetTypeName(long typeId)
    {
        if (_typeNames.TryGetValue(typeId, out var cached))
            return cached;

        using var connection = new DuckDBConnection(_database.ConnectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT name FROM main.sde_types WHERE type_id = {typeId} LIMIT 1;";

        var name = command.ExecuteScalar() as string;

        if (name is not null)
            _typeNames[typeId] = name;

        return name;
    }

    public string? GetSolarSystemName(long systemId)
    {
        if (_solarSystemNames.TryGetValue(systemId, out var cached))
            return cached;

        using var connection = new DuckDBConnection(_database.ConnectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT name FROM main.sde_solar_systems WHERE system_id = {systemId} LIMIT 1;";

        var name = command.ExecuteScalar() as string;

        if (name is not null)
            _solarSystemNames[systemId] = name;

        return name;
    }

    public string? GetFactionName(long factionId)
    {
        var names = _factionNames;

        if (names is null)
        {
            try
            {
                using var connection = new DuckDBConnection(_database.ConnectionString);
                connection.Open();

                using var command = connection.CreateCommand();
                command.CommandText = "SELECT faction_id, name FROM main.sde_factions;";

                using var reader = command.ExecuteReader();
                var loaded = new Dictionary<long, string>();

                while (reader.Read())
                {
                    if (!reader.IsDBNull(1))
                        loaded[reader.GetInt64(0)] = reader.GetString(1);
                }

                if (loaded.Count == 0)
                    return null;

                _factionNames = loaded;
                names = loaded;
            }
            catch (Exception exception)
            {
                EngineFailureLog.Record($"SDE faction lookup failed, faction names unavailable: {exception.Message}");
                return null;
            }
        }

        return names.TryGetValue(factionId, out var name) ? name : null;
    }

    public bool IsNpcCorporation(long corporationId)
    {
        using var connection = new DuckDBConnection(_database.ConnectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT 1 FROM main.sde_npc_corporations WHERE corporation_id = {corporationId} LIMIT 1;";

        return command.ExecuteScalar() is not null;
    }

    public IReadOnlySet<long> GetNpcCorporationIds()
    {
        var cached = _npcCorporationIds;

        if (cached is not null)
            return cached;

        try
        {
            using var connection = new DuckDBConnection(_database.ConnectionString);
            connection.Open();

            using var command = connection.CreateCommand();
            command.CommandText = "SELECT corporation_id FROM main.sde_npc_corporations;";

            using var reader = command.ExecuteReader();
            var ids = new HashSet<long>();

            while (reader.Read())
                ids.Add(reader.GetInt64(0));

            if (ids.Count > 0)
                _npcCorporationIds = ids;

            return ids;
        }
        catch (Exception exception)
        {
            EngineFailureLog.Record($"SDE NPC corporation lookup failed, same-group count treating no corporation as NPC: {exception.Message}");
            return new HashSet<long>();
        }
    }

    public Task<bool> HasReferenceDataAsync(CancellationToken cancellationToken = default)
    {
        using var connection = new DuckDBConnection(_database.ConnectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = """
                              SELECT (EXISTS (SELECT 1 FROM main.sde_types)
                                      OR EXISTS (SELECT 1 FROM main.sde_solar_systems)
                                      OR EXISTS (SELECT 1 FROM main.sde_npc_corporations))
                                  AND EXISTS (SELECT 1 FROM main.sde_factions);
                              """;

        return Task.FromResult(Convert.ToBoolean(command.ExecuteScalar()));
    }

    public Task<SdeMetadata> GetMetadataAsync(CancellationToken cancellationToken = default)
    {
        using var connection = new DuckDBConnection(_database.ConnectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = """
                              SELECT build_number, last_checked_utc, last_updated_utc, last_attempt_utc, last_check_result
                              FROM main.sde_metadata
                              LIMIT 1;
                              """;

        using var reader = command.ExecuteReader();

        if (!reader.Read())
            return Task.FromResult(new SdeMetadata(null, null, null, null, null));

        return Task.FromResult(new SdeMetadata(
            reader.GetNullableInt64(0),
            reader.GetNullableDateTimeOffset(1),
            reader.GetNullableDateTimeOffset(2),
            reader.GetNullableDateTimeOffset(3),
            reader.GetNullableString(4)));
    }

    public Task ReplaceTablesAsync(SdeReplacementData data, CancellationToken cancellationToken = default)
    {
        using var connection = new DuckDBConnection(_database.ConnectionString);
        connection.Open();

        using var transaction = connection.BeginTransaction();

        CreateStagingTable(connection, transaction, "sde_types_staging", "type_id BIGINT PRIMARY KEY, name TEXT NOT NULL");
        InsertTypes(connection, transaction, "sde_types_staging", data.Types);

        CreateStagingTable(connection, transaction, "sde_solar_systems_staging", "system_id BIGINT PRIMARY KEY, name TEXT NOT NULL");
        InsertSolarSystems(connection, transaction, "sde_solar_systems_staging", data.SolarSystems);

        CreateStagingTable(connection, transaction, "sde_npc_corporations_staging", "corporation_id BIGINT PRIMARY KEY");
        InsertNpcCorporationIds(connection, transaction, "sde_npc_corporations_staging", data.NpcCorporationIds);

        CreateStagingTable(connection, transaction, "sde_factions_staging", "faction_id BIGINT PRIMARY KEY, name TEXT");
        InsertFactions(connection, transaction, "sde_factions_staging", data.Factions ?? []);

        SwapStagingTable(connection, transaction, "sde_types");
        SwapStagingTable(connection, transaction, "sde_solar_systems");
        SwapStagingTable(connection, transaction, "sde_npc_corporations");
        SwapStagingTable(connection, transaction, "sde_factions");

        using (var updateMetadata = connection.CreateCommand())
        {
            updateMetadata.Transaction = transaction;
            updateMetadata.CommandText = $"""
                                UPDATE main.sde_metadata
                                SET build_number = {data.BuildNumber},
                                    last_checked_utc = {SqlValueFormatter.Date(data.UpdatedUtc)},
                                    last_updated_utc = {SqlValueFormatter.Date(data.UpdatedUtc)},
                                    last_attempt_utc = {SqlValueFormatter.Date(data.UpdatedUtc)},
                                    last_check_result = 'Replaced';
                                """;
            updateMetadata.ExecuteNonQuery();
        }

        transaction.Commit();

        _typeNames.Clear();
        _solarSystemNames.Clear();
        _npcCorporationIds = null;
        _factionNames = null;

        return Task.CompletedTask;
    }

    public Task RecordCheckAsync(DateTimeOffset attemptedUtc, string checkResult, bool succeeded, CancellationToken cancellationToken = default)
    {
        using var connection = new DuckDBConnection(_database.ConnectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = succeeded
            ? $"""
              UPDATE main.sde_metadata
              SET last_checked_utc = {SqlValueFormatter.Date(attemptedUtc)},
                  last_attempt_utc = {SqlValueFormatter.Date(attemptedUtc)},
                  last_check_result = {SqlValueFormatter.String(checkResult)};
              """
            : $"""
              UPDATE main.sde_metadata
              SET last_attempt_utc = {SqlValueFormatter.Date(attemptedUtc)},
                  last_check_result = {SqlValueFormatter.String(checkResult)};
              """;
        command.ExecuteNonQuery();

        return Task.CompletedTask;
    }

    private static void CreateStagingTable(
        DuckDBConnection connection,
        DuckDBTransaction transaction,
        string tableName,
        string columns)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"CREATE OR REPLACE TABLE main.{tableName} ({columns});";
        command.ExecuteNonQuery();
    }

    private static void SwapStagingTable(
        DuckDBConnection connection,
        DuckDBTransaction transaction,
        string tableName)
    {
        using (var drop = connection.CreateCommand())
        {
            drop.Transaction = transaction;
            drop.CommandText = $"DROP TABLE IF EXISTS main.{tableName};";
            drop.ExecuteNonQuery();
        }

        using (var rename = connection.CreateCommand())
        {
            rename.Transaction = transaction;
            rename.CommandText = $"ALTER TABLE main.{tableName}_staging RENAME TO {tableName};";
            rename.ExecuteNonQuery();
        }
    }

    private static void InsertTypes(
        DuckDBConnection connection,
        DuckDBTransaction transaction,
        string tableName,
        IReadOnlyList<SdeType> types)
    {
        for (var offset = 0; offset < types.Count; offset += InsertBatchSize)
        {
            var values = string.Join(
                ",",
                types.Skip(offset).Take(InsertBatchSize)
                    .Select(row => $"({row.TypeId}, {SqlValueFormatter.String(row.Name)})"));

            if (values.Length == 0)
                continue;

            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = $"INSERT INTO main.{tableName} (type_id, name) VALUES {values};";
            command.ExecuteNonQuery();
        }
    }

    private static void InsertSolarSystems(
        DuckDBConnection connection,
        DuckDBTransaction transaction,
        string tableName,
        IReadOnlyList<SdeSolarSystem> solarSystems)
    {
        for (var offset = 0; offset < solarSystems.Count; offset += InsertBatchSize)
        {
            var values = string.Join(
                ",",
                solarSystems.Skip(offset).Take(InsertBatchSize)
                    .Select(row => $"({row.SystemId}, {SqlValueFormatter.String(row.Name)})"));

            if (values.Length == 0)
                continue;

            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = $"INSERT INTO main.{tableName} (system_id, name) VALUES {values};";
            command.ExecuteNonQuery();
        }
    }

    private static void InsertFactions(
        DuckDBConnection connection,
        DuckDBTransaction transaction,
        string tableName,
        IReadOnlyList<SdeFaction> factions)
    {
        for (var offset = 0; offset < factions.Count; offset += InsertBatchSize)
        {
            var values = string.Join(
                ",",
                factions.Skip(offset).Take(InsertBatchSize)
                    .Select(row => $"({row.FactionId}, {SqlValueFormatter.String(row.Name)})"));

            if (values.Length == 0)
                continue;

            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = $"INSERT INTO main.{tableName} (faction_id, name) VALUES {values};";
            command.ExecuteNonQuery();
        }
    }

    private static void InsertNpcCorporationIds(
        DuckDBConnection connection,
        DuckDBTransaction transaction,
        string tableName,
        IReadOnlyList<long> corporationIds)
    {
        for (var offset = 0; offset < corporationIds.Count; offset += InsertBatchSize)
        {
            var values = string.Join(
                ",",
                corporationIds.Skip(offset).Take(InsertBatchSize)
                    .Select(id => $"({id})"));

            if (values.Length == 0)
                continue;

            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = $"INSERT INTO main.{tableName} (corporation_id) VALUES {values};";
            command.ExecuteNonQuery();
        }
    }
}

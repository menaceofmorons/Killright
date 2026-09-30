using DuckDB.NET.Data;
using Killright.Core.Models;
using Killright.Shared;
using Killright.Shared.Data;
using Killright.Storage.Database;

namespace Killright.Storage.Identity;

public sealed class DuckDbPilotIdentityCache : IPilotIdentityCache
{
    private readonly KillRightDatabase _database;

    public DuckDbPilotIdentityCache(KillRightDatabase database)
    {
        _database = database;
    }

    public Task<Pilot?> GetAsync(string inputName, TimeSpan maximumAge, CancellationToken cancellationToken = default)
    {
        var normalizedInputName = PilotIdentityCacheRecord.NormalizeInputName(inputName);

        using var connection = new DuckDBConnection(_database.ConnectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = $"""
                              SELECT input_name,
                                     character_id,
                                     character_name,
                                     verify_status,
                                     security_status,
                                     corporation_id,
                                     corporation_name,
                                     corporation_ticker,
                                     alliance_id,
                                     alliance_name,
                                     alliance_ticker,
                                     cached_at_utc,
                                     birthday
                              FROM pilot_identity_cache
                              WHERE input_name = {SqlValueFormatter.String(normalizedInputName)}
                              LIMIT 1;
                              """;

        using var reader = command.ExecuteReader();

        if (!reader.Read())
            return Task.FromResult<Pilot?>(null);

        var cachedAtUtc = reader.GetDateTime(11);

        if (DateTime.UtcNow - cachedAtUtc > maximumAge)
            return Task.FromResult<Pilot?>(null);

        var record = new PilotIdentityCacheRecord
        {
            InputName = reader.GetString(0),
            CharacterId = reader.GetNullableInt64(1),
            CharacterName = reader.GetNullableString(2),
            VerifyStatus = Enum.Parse<VerifyStatus>(reader.GetString(3)),
            SecurityStatus = reader.GetNullableDouble(4),
            CorporationId = reader.GetNullableInt64(5),
            CorporationName = reader.GetNullableString(6),
            CorporationTicker = reader.GetNullableString(7),
            AllianceId = reader.GetNullableInt64(8),
            AllianceName = reader.GetNullableString(9),
            AllianceTicker = reader.GetNullableString(10),
            CachedAtUtc = cachedAtUtc,
            Birthday = reader.IsDBNull(12) ? null : DateOnly.FromDateTime(reader.GetDateTime(12))
        };

        return Task.FromResult<Pilot?>(record.ToPilot());
    }

    public Task<DateOnly?> GetBirthdayAsync(string inputName, CancellationToken cancellationToken = default)
    {
        var normalizedInputName = PilotIdentityCacheRecord.NormalizeInputName(inputName);

        using var connection = new DuckDBConnection(_database.ConnectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = $"""
                              SELECT birthday
                              FROM pilot_identity_cache
                              WHERE input_name = {SqlValueFormatter.String(normalizedInputName)}
                              LIMIT 1;
                              """;

        using var reader = command.ExecuteReader();

        if (!reader.Read() || reader.IsDBNull(0))
            return Task.FromResult<DateOnly?>(null);

        return Task.FromResult<DateOnly?>(DateOnly.FromDateTime(reader.GetDateTime(0)));
    }

    public Task<IReadOnlyDictionary<string, PilotIdentityCacheRecord>> GetRecordsAsync(
        IReadOnlyCollection<string> inputNames,
        ScanDatabaseSession? session = null,
        CancellationToken cancellationToken = default)
    {
        var records = new Dictionary<string, PilotIdentityCacheRecord>(StringComparer.Ordinal);
        var normalizedNames = inputNames
            .Select(PilotIdentityCacheRecord.NormalizeInputName)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (normalizedNames.Count == 0)
            return Task.FromResult<IReadOnlyDictionary<string, PilotIdentityCacheRecord>>(records);

        using var ownedConnection = session is null ? _database.OpenConnection() : null;
        var connection = session?.Connection ?? ownedConnection!;

        foreach (var chunk in normalizedNames.Chunk(500))
        {
            using var command = connection.CreateCommand();
            command.CommandText = $"""
                                  SELECT input_name,
                                         character_id,
                                         character_name,
                                         verify_status,
                                         security_status,
                                         corporation_id,
                                         corporation_name,
                                         corporation_ticker,
                                         alliance_id,
                                         alliance_name,
                                         alliance_ticker,
                                         cached_at_utc,
                                         birthday,
                                         security_status_at_utc
                                  FROM pilot_identity_cache
                                  WHERE input_name IN ({string.Join(", ", chunk.Select(SqlValueFormatter.String))});
                                  """;

            using var reader = command.ExecuteReader();

            while (reader.Read())
            {
                var record = new PilotIdentityCacheRecord
                {
                    InputName = reader.GetString(0),
                    CharacterId = reader.GetNullableInt64(1),
                    CharacterName = reader.GetNullableString(2),
                    VerifyStatus = Enum.Parse<VerifyStatus>(reader.GetString(3)),
                    SecurityStatus = reader.GetNullableDouble(4),
                    CorporationId = reader.GetNullableInt64(5),
                    CorporationName = reader.GetNullableString(6),
                    CorporationTicker = reader.GetNullableString(7),
                    AllianceId = reader.GetNullableInt64(8),
                    AllianceName = reader.GetNullableString(9),
                    AllianceTicker = reader.GetNullableString(10),
                    CachedAtUtc = reader.GetDateTime(11),
                    Birthday = reader.IsDBNull(12) ? null : DateOnly.FromDateTime(reader.GetDateTime(12)),
                    SecurityStatusAtUtc = reader.IsDBNull(13) ? null : reader.GetDateTime(13)
                };

                records[record.InputName] = record;
            }
        }

        return Task.FromResult<IReadOnlyDictionary<string, PilotIdentityCacheRecord>>(records);
    }

    public Task<PilotIdentityCacheRecord?> GetRecordAsync(string inputName, CancellationToken cancellationToken = default)
    {
        var normalizedInputName = PilotIdentityCacheRecord.NormalizeInputName(inputName);

        using var connection = new DuckDBConnection(_database.ConnectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = $"""
                              SELECT input_name,
                                     character_id,
                                     character_name,
                                     verify_status,
                                     security_status,
                                     corporation_id,
                                     corporation_name,
                                     corporation_ticker,
                                     alliance_id,
                                     alliance_name,
                                     alliance_ticker,
                                     cached_at_utc,
                                     birthday,
                                     security_status_at_utc
                              FROM pilot_identity_cache
                              WHERE input_name = {SqlValueFormatter.String(normalizedInputName)}
                              LIMIT 1;
                              """;

        using var reader = command.ExecuteReader();

        if (!reader.Read())
            return Task.FromResult<PilotIdentityCacheRecord?>(null);

        var record = new PilotIdentityCacheRecord
        {
            InputName = reader.GetString(0),
            CharacterId = reader.GetNullableInt64(1),
            CharacterName = reader.GetNullableString(2),
            VerifyStatus = Enum.Parse<VerifyStatus>(reader.GetString(3)),
            SecurityStatus = reader.GetNullableDouble(4),
            CorporationId = reader.GetNullableInt64(5),
            CorporationName = reader.GetNullableString(6),
            CorporationTicker = reader.GetNullableString(7),
            AllianceId = reader.GetNullableInt64(8),
            AllianceName = reader.GetNullableString(9),
            AllianceTicker = reader.GetNullableString(10),
            CachedAtUtc = reader.GetDateTime(11),
            Birthday = reader.IsDBNull(12) ? null : DateOnly.FromDateTime(reader.GetDateTime(12)),
            SecurityStatusAtUtc = reader.IsDBNull(13) ? null : reader.GetDateTime(13)
        };

        return Task.FromResult<PilotIdentityCacheRecord?>(record);
    }

    public Task UpsertAsync(Pilot pilot, CancellationToken cancellationToken = default)
    {
        if (!IsDefinitive(pilot))
            return Task.CompletedTask;

        return UpsertRecordAsync(PilotIdentityCacheRecord.FromPilot(pilot), cancellationToken);
    }

    public Task UpsertRecordAsync(PilotIdentityCacheRecord record, CancellationToken cancellationToken = default)
    {
        using var connection = new DuckDBConnection(_database.ConnectionString);
        connection.Open();

        using (var deleteCommand = connection.CreateCommand())
        {
            deleteCommand.CommandText = $"DELETE FROM pilot_identity_cache WHERE input_name = {SqlValueFormatter.String(record.InputName)};";
            deleteCommand.ExecuteNonQuery();
        }

        using (var insertCommand = connection.CreateCommand())
        {
            insertCommand.CommandText = $"""
                                        INSERT INTO pilot_identity_cache (
                                            input_name,
                                            character_id,
                                            character_name,
                                            verify_status,
                                            security_status,
                                            corporation_id,
                                            corporation_name,
                                            corporation_ticker,
                                            alliance_id,
                                            alliance_name,
                                            alliance_ticker,
                                            cached_at_utc,
                                            birthday,
                                            security_status_at_utc
                                        ) VALUES (
                                            {SqlValueFormatter.String(record.InputName)},
                                            {SqlValueFormatter.Long(record.CharacterId)},
                                            {SqlValueFormatter.String(record.CharacterName)},
                                            {SqlValueFormatter.String(record.VerifyStatus.ToString())},
                                            {SqlValueFormatter.Double(record.SecurityStatus)},
                                            {SqlValueFormatter.Long(record.CorporationId)},
                                            {SqlValueFormatter.String(record.CorporationName)},
                                            {SqlValueFormatter.String(record.CorporationTicker)},
                                            {SqlValueFormatter.Long(record.AllianceId)},
                                            {SqlValueFormatter.String(record.AllianceName)},
                                            {SqlValueFormatter.String(record.AllianceTicker)},
                                            {SqlValueFormatter.Date(record.CachedAtUtc)},
                                            {SqlValueFormatter.Date(record.Birthday)},
                                            {(record.SecurityStatusAtUtc is { } securityStatusAtUtc ? SqlValueFormatter.Date(securityStatusAtUtc) : "NULL")}
                                        );
                                        """;
            insertCommand.ExecuteNonQuery();
        }

        return Task.CompletedTask;
    }

    private static bool IsDefinitive(Pilot pilot)
    {
        if (pilot.VerifyStatus == VerifyStatus.NoMatch)
            return true;

        return pilot.VerifyStatus == VerifyStatus.Partial
            && pilot.CharacterId is not null
            && pilot.Corporation is not null
            && (pilot.AllianceId is null || pilot.Alliance is not null);
    }
}

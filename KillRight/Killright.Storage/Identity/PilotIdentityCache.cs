using Microsoft.Data.Sqlite;
using Killright.Core.Models;
using Killright.Shared;
using Killright.Shared.Data;
using Killright.Storage.Database;

namespace Killright.Storage.Identity;

public sealed class PilotIdentityCache : IPilotIdentityCache
{
    private readonly KillRightDatabase _database;

    public PilotIdentityCache(KillRightDatabase database)
    {
        _database = database;
    }

    public Task<Pilot?> GetAsync(string inputName, TimeSpan maximumAge, CancellationToken cancellationToken = default)
    {
        var normalizedInputName = PilotIdentityCacheRecord.NormalizeInputName(inputName);

        using var connection = _database.OpenConnection();

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
                                     faction_id
                              FROM pilot_identity_cache
                              WHERE input_name = {SqlValueFormatter.String(normalizedInputName)}
                              LIMIT 1;
                              """;

        using var reader = command.ExecuteReader();

        if (!reader.Read())
            return Task.FromResult<Pilot?>(null);

        var cachedAtUtc = reader.GetUtcDateTimeOffset(11).UtcDateTime;

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
            Birthday = reader.GetNullableDateOnly(12),
            FactionId = reader.GetNullableInt64(13)
        };

        return Task.FromResult<Pilot?>(record.ToPilot());
    }

    public Task<DateOnly?> GetBirthdayAsync(string inputName, CancellationToken cancellationToken = default)
    {
        var normalizedInputName = PilotIdentityCacheRecord.NormalizeInputName(inputName);

        using var connection = _database.OpenConnection();

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

        return Task.FromResult<DateOnly?>(reader.GetDateOnly(0));
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
                                         security_status_at_utc,
                                         faction_id
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
                    CachedAtUtc = reader.GetUtcDateTimeOffset(11).UtcDateTime,
                    Birthday = reader.GetNullableDateOnly(12),
                    SecurityStatusAtUtc = reader.GetNullableDateTimeOffset(13)?.UtcDateTime,
                    FactionId = reader.GetNullableInt64(14)
                };

                records[record.InputName] = record;
            }
        }

        return Task.FromResult<IReadOnlyDictionary<string, PilotIdentityCacheRecord>>(records);
    }

    public Task<PilotIdentityCacheRecord?> GetRecordAsync(string inputName, CancellationToken cancellationToken = default)
    {
        var normalizedInputName = PilotIdentityCacheRecord.NormalizeInputName(inputName);

        using var connection = _database.OpenConnection();

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
                                     security_status_at_utc,
                                     faction_id
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
            CachedAtUtc = reader.GetUtcDateTimeOffset(11).UtcDateTime,
            Birthday = reader.GetNullableDateOnly(12),
            SecurityStatusAtUtc = reader.GetNullableDateTimeOffset(13)?.UtcDateTime,
            FactionId = reader.GetNullableInt64(14)
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
        using var scope = _database.BeginWrite();

        using (var deleteCommand = scope.Connection.CreateCommand())
        {
            deleteCommand.Transaction = scope.Transaction;
            deleteCommand.CommandText = $"DELETE FROM pilot_identity_cache WHERE input_name = {SqlValueFormatter.String(record.InputName)};";
            deleteCommand.ExecuteNonQuery();
        }

        using (var insertCommand = scope.Connection.CreateCommand())
        {
            insertCommand.Transaction = scope.Transaction;
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
                                            security_status_at_utc,
                                            faction_id
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
                                            {(record.SecurityStatusAtUtc is { } securityStatusAtUtc ? SqlValueFormatter.Date(securityStatusAtUtc) : "NULL")},
                                            {SqlValueFormatter.Long(record.FactionId)}
                                        );
                                        """;
            insertCommand.ExecuteNonQuery();
        }

        scope.Commit();

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

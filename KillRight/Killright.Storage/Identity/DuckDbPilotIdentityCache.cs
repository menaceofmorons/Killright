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
                                     cached_at_utc
                              FROM pilot_identity_cache
                              WHERE input_name = {SqlValueFormatter.String(inputName)}
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
            CachedAtUtc = cachedAtUtc
        };

        return Task.FromResult<Pilot?>(record.ToPilot());
    }

    public Task UpsertAsync(Pilot pilot, CancellationToken cancellationToken = default)
    {
        var record = PilotIdentityCacheRecord.FromPilot(pilot);

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
                                            cached_at_utc
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
                                            {SqlValueFormatter.Date(record.CachedAtUtc)}
                                        );
                                        """;
            insertCommand.ExecuteNonQuery();
        }

        return Task.CompletedTask;
    }
}
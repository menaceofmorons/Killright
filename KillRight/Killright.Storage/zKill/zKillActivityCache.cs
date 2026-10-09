using Microsoft.Data.Sqlite;
using Killright.Integration.zKill;
using Killright.Shared.Data;
using Killright.Shared.zKill;
using Killright.Storage.Database;

namespace Killright.Storage.zKill;


public sealed class zKillActivityCache : IzKillActivityCache
{
    private readonly KillRightDatabase _database;

    public zKillActivityCache(KillRightDatabase database)
    {
        _database = database;
    }

    public Task<zKillActivity?> GetAsync(long characterId, CancellationToken cancellationToken = default)
    {
        using var connection = _database.OpenConnection();

        using var command = connection.CreateCommand();
        command.CommandText = $"""
                              SELECT character_id,
                                     has_public_activity_data,
                                     kills_week,
                                     solo_week,
                                     last_active_utc,
                                     last_activity_type,
                                     checked_at_utc,
                                     error,
                                     last_recent_call_utc,
                                     recent_coverage_start_utc,
                                     last_kill_utc
                              FROM zkill_activity_cache
                              WHERE character_id = {characterId}
                              LIMIT 1;
                              """;

        using var reader = command.ExecuteReader();

        if (!reader.Read())
            return Task.FromResult<zKillActivity?>(null);

        var checkedAtUtc = reader.GetUtcDateTimeOffset(6);

        var record = new zKillActivityCacheRecord
        {
            CharacterId = reader.GetInt64(0),
            HasPublicActivityData = reader.GetBoolean(1),
            KillsWeek = reader.GetNullableInt32(2),
            SoloWeek = reader.GetNullableInt32(3),
            LastActiveUtc = reader.GetNullableDateTimeOffset(4),
            LastActivityType = ReadActivityTypeOrNull(reader.GetNullableString(5)),
            CheckedAtUtc = checkedAtUtc,
            Error = reader.GetNullableString(7),
            LastSuccessfulRecentCallUtc = reader.GetNullableDateTimeOffset(8),
            RecentCoverageStartUtc = reader.GetNullableDateTimeOffset(9),
            LastKillUtc = reader.GetNullableDateTimeOffset(10)
        };

        return Task.FromResult<zKillActivity?>(record.ToActivity());
    }

    public Task<IReadOnlyDictionary<long, zKillActivity>> GetManyAsync(
        IReadOnlyCollection<long> characterIds,
        ScanDatabaseSession? session = null,
        CancellationToken cancellationToken = default)
    {
        var results = new Dictionary<long, zKillActivity>();

        if (characterIds.Count == 0)
            return Task.FromResult<IReadOnlyDictionary<long, zKillActivity>>(results);

        using var ownedConnection = session is null ? _database.OpenConnection() : null;
        var connection = session?.Connection ?? ownedConnection!;

        foreach (var chunk in characterIds.Distinct().Chunk(500))
        {
            using var command = connection.CreateCommand();
            command.CommandText = $"""
                                  SELECT character_id,
                                         has_public_activity_data,
                                         kills_week,
                                         solo_week,
                                         last_active_utc,
                                         last_activity_type,
                                         checked_at_utc,
                                         error,
                                         last_recent_call_utc,
                                         recent_coverage_start_utc,
                                         last_kill_utc
                                  FROM zkill_activity_cache
                                  WHERE character_id IN ({string.Join(", ", chunk)});
                                  """;

            using var reader = command.ExecuteReader();

            while (reader.Read())
            {
                var record = new zKillActivityCacheRecord
                {
                    CharacterId = reader.GetInt64(0),
                    HasPublicActivityData = reader.GetBoolean(1),
                    KillsWeek = reader.GetNullableInt32(2),
                    SoloWeek = reader.GetNullableInt32(3),
                    LastActiveUtc = reader.GetNullableDateTimeOffset(4),
                    LastActivityType = ReadActivityTypeOrNull(reader.GetNullableString(5)),
                    CheckedAtUtc = reader.GetUtcDateTimeOffset(6),
                    Error = reader.GetNullableString(7),
                    LastSuccessfulRecentCallUtc = reader.GetNullableDateTimeOffset(8),
                    RecentCoverageStartUtc = reader.GetNullableDateTimeOffset(9),
                    LastKillUtc = reader.GetNullableDateTimeOffset(10)
                };

                results[record.CharacterId] = record.ToActivity();
            }
        }

        return Task.FromResult<IReadOnlyDictionary<long, zKillActivity>>(results);
    }

    public Task UpsertAsync(zKillActivity activity, CancellationToken cancellationToken = default)
    {
        var record = zKillActivityCacheRecord.FromActivity(activity);

        using var scope = _database.BeginWrite();

        using (var deleteCommand = scope.Connection.CreateCommand())
        {
            deleteCommand.Transaction = scope.Transaction;
            deleteCommand.CommandText = $"DELETE FROM zkill_activity_cache WHERE character_id = {record.CharacterId};";
            deleteCommand.ExecuteNonQuery();
        }

        using (var insertCommand = scope.Connection.CreateCommand())
        {
            insertCommand.Transaction = scope.Transaction;
            insertCommand.CommandText = $"""
                                        INSERT INTO zkill_activity_cache (
                                            character_id,
                                            has_public_activity_data,
                                            kills_week,
                                            solo_week,
                                            last_active_utc,
                                            last_activity_type,
                                            checked_at_utc,
                                            error,
                                            last_recent_call_utc,
                                            recent_coverage_start_utc,
                                            last_kill_utc
                                        ) VALUES (
                                            {record.CharacterId},
                                            {SqlValueFormatter.Bool(record.HasPublicActivityData)},
                                            {SqlValueFormatter.Int(record.KillsWeek)},
                                            {SqlValueFormatter.Int(record.SoloWeek)},
                                            {SqlValueFormatter.Date(record.LastActiveUtc)},
                                            {SqlValueFormatter.String(record.LastActivityType?.ToString())},
                                            {SqlValueFormatter.Date(record.CheckedAtUtc)},
                                            {SqlValueFormatter.String(record.Error)},
                                            {SqlValueFormatter.Date(record.LastSuccessfulRecentCallUtc)},
                                            {SqlValueFormatter.Date(record.RecentCoverageStartUtc)},
                                            {SqlValueFormatter.Date(record.LastKillUtc)}
                                        );
                                        """;
            insertCommand.ExecuteNonQuery();
        }

        scope.Commit();

        return Task.CompletedTask;
    }

    private static zKillActivityType? ReadActivityTypeOrNull(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        return Enum.TryParse<zKillActivityType>(value, out var result) ? result : null;
    }
}
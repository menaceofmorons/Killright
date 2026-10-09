using System.Globalization;
using Microsoft.Data.Sqlite;
using Killright.Shared.Data;
using Killright.Storage.Database;
using Killright.Storage.Diagnostics;
using Killright.Storage.Sde;

namespace Killright.Storage.Engine;

public sealed class EngineInputReader : IEngineInputReader
{
    private readonly KillRightDatabase _database;
    private readonly ISdeReferenceDataStore _sdeReferenceDataStore;
    private readonly Action<string>? _logFailure;

    public EngineInputReader(
        KillRightDatabase database,
        ISdeReferenceDataStore sdeReferenceDataStore,
        Action<string>? logFailure = null)
    {
        _database = database;
        _sdeReferenceDataStore = sdeReferenceDataStore;
        _logFailure = logFailure;
    }

    public Task<IReadOnlyList<EnginePilotInputsResult>> ReadPilotInputsAsync(
        IReadOnlyList<long> characterIds,
        ScanTimings? timings = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.Run(() => ReadPilotInputs(characterIds, timings));
    }

    public Task<EngineGroupInputsResult> ReadGroupInputsAsync(
        IReadOnlyList<long> scannedCharacterIds,
        ScanTimings? timings = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.Run(() => ReadGroupInputs(scannedCharacterIds, timings));
    }

    private IReadOnlyList<EnginePilotInputsResult> ReadPilotInputs(IReadOnlyList<long> characterIds, ScanTimings? timings)
    {
        if (characterIds.Count == 0)
            return Array.Empty<EnginePilotInputsResult>();

        using (timings.Measure(ScanTimings.EngineLevel, "engine_input_read"))
        {
            SqliteConnection connection;

            try
            {
                connection = _database.OpenConnection();
            }
            catch (Exception exception)
            {
                return FailAll(characterIds, EngineInputFailureReasons.Killmails, exception);
            }

            using (connection)
            {
                Dictionary<long, List<EngineKillmailRow>> killmails;
                Dictionary<long, EngineStatisticsRow> statistics;
                Dictionary<long, EngineIdentityRow> identities;
                Dictionary<long, string> coverageStarts;

                try
                {
                    using (timings.Measure(ScanTimings.EngineLevel, "killmails_read"))
                        killmails = ReadKillmails(connection, characterIds);
                }
                catch (Exception exception)
                {
                    return FailAll(characterIds, EngineInputFailureReasons.Killmails, exception);
                }

                try
                {
                    using (timings.Measure(ScanTimings.EngineLevel, "statistics_read"))
                        statistics = ReadStatistics(connection, characterIds);
                }
                catch (Exception exception)
                {
                    return FailAll(characterIds, EngineInputFailureReasons.Statistics, exception);
                }

                try
                {
                    using (timings.Measure(ScanTimings.EngineLevel, "identity_read"))
                        identities = FirstByCharacter(ReadIdentities(connection, characterIds));
                }
                catch (Exception exception)
                {
                    return FailAll(characterIds, EngineInputFailureReasons.Identity, exception);
                }

                try
                {
                    using (timings.Measure(ScanTimings.EngineLevel, "activity_cache_read"))
                        coverageStarts = ReadCoverageStarts(connection, characterIds);
                }
                catch (Exception exception)
                {
                    return FailAll(characterIds, EngineInputFailureReasons.ActivityCache, exception);
                }

                return characterIds
                    .Select(characterId => new EnginePilotInputsResult(
                        characterId,
                        new EnginePilotInputs(
                            characterId,
                            killmails.TryGetValue(characterId, out var rows) ? rows : new List<EngineKillmailRow>(),
                            statistics.GetValueOrDefault(characterId),
                            identities.GetValueOrDefault(characterId),
                            coverageStarts.GetValueOrDefault(characterId)),
                        null))
                    .ToList();
            }
        }
    }

    private EngineGroupInputsResult ReadGroupInputs(IReadOnlyList<long> scannedCharacterIds, ScanTimings? timings)
    {
        using (timings.Measure(ScanTimings.EngineLevel, "group_input_read"))
        {
            SqliteConnection connection;

            try
            {
                connection = _database.OpenConnection();
            }
            catch (Exception exception)
            {
                return FailGroup(EngineInputFailureReasons.DirectEvidence, exception);
            }

            IReadOnlyList<EngineAttackerEvidenceRow> directEvidence;
            IReadOnlyList<EngineAttackerEvidenceRow> chainEvidence;
            IReadOnlyList<EngineIdentityRow> identities;

            using (connection)
            {
                try
                {
                    using (timings.Measure(ScanTimings.EngineLevel, "direct_evidence_read"))
                        directEvidence = ReadAttackerEvidence(connection, scannedCharacterIds, qualifyingOnly: false);
                }
                catch (Exception exception)
                {
                    return FailGroup(EngineInputFailureReasons.DirectEvidence, exception);
                }

                try
                {
                    using (timings.Measure(ScanTimings.EngineLevel, "chain_evidence_read"))
                        chainEvidence = ReadAttackerEvidence(connection, scannedCharacterIds, qualifyingOnly: true);
                }
                catch (Exception exception)
                {
                    return FailGroup(EngineInputFailureReasons.ChainEvidence, exception);
                }

                try
                {
                    using (timings.Measure(ScanTimings.EngineLevel, "identities_read"))
                        identities = ReadIdentities(connection, scannedCharacterIds);
                }
                catch (Exception exception)
                {
                    return FailGroup(EngineInputFailureReasons.Identity, exception);
                }
            }

            IReadOnlyCollection<long> npcCorporationIds;

            try
            {
                npcCorporationIds = _sdeReferenceDataStore.GetNpcCorporationIds();
            }
            catch (Exception exception)
            {
                return FailGroup(EngineInputFailureReasons.NpcCorporations, exception);
            }

            return new EngineGroupInputsResult(
                new EngineGroupInputs(directEvidence, chainEvidence, identities, npcCorporationIds),
                null);
        }
    }

    private List<EnginePilotInputsResult> FailAll(IReadOnlyList<long> characterIds, string reason, Exception exception)
    {
        Log(reason, exception);

        return characterIds
            .Select(characterId => new EnginePilotInputsResult(characterId, null, reason))
            .ToList();
    }

    private EngineGroupInputsResult FailGroup(string reason, Exception exception)
    {
        Log(reason, exception);

        return new EngineGroupInputsResult(null, reason);
    }

    private void Log(string reason, Exception exception)
    {
        try
        {
            _logFailure?.Invoke($"engine input read failed ({reason}): {exception.Message}");
        }
        catch
        {
        }
    }

    private static string FormatEngineTime(DateTimeOffset value)
    {
        return value.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);
    }

    private static string IdList(IReadOnlyList<long> characterIds)
    {
        return string.Join(",", characterIds.Select(characterId => characterId.ToString(CultureInfo.InvariantCulture)));
    }

    private static Dictionary<long, List<EngineKillmailRow>> ReadKillmails(
        SqliteConnection connection,
        IReadOnlyList<long> characterIds)
    {
        var grouped = new Dictionary<long, List<EngineKillmailRow>>();
        var ids = IdList(characterIds);

        using var command = connection.CreateCommand();
        command.CommandText = $"""
                               SELECT victim_character_id AS character_id, killmail_id, killmail_hash, kill_time_utc,
                                      1 AS is_loss, unique_attacker_count, is_solo, victim_ship_type_id, system_id,
                                      location_id, is_npc, cached_at_utc
                               FROM main.zkill_killmails
                               WHERE victim_character_id IN ({ids})
                               UNION ALL
                               SELECT a.character_id, k.killmail_id, k.killmail_hash, k.kill_time_utc,
                                      0 AS is_loss, k.unique_attacker_count, k.is_solo, k.victim_ship_type_id, k.system_id,
                                      k.location_id, k.is_npc, k.cached_at_utc
                               FROM main.zkill_killmail_attackers a
                               JOIN main.zkill_killmails k ON k.killmail_id = a.killmail_id
                               WHERE a.character_id IN ({ids})
                               ORDER BY kill_time_utc DESC;
                               """;

        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            var row = new EngineKillmailRow(
                KillmailId: reader.GetInt64(1),
                KillmailHash: reader.GetNullableString(2),
                CharacterId: reader.GetInt64(0),
                KillTimeUtc: FormatEngineTime(reader.GetUtcDateTimeOffset(3)),
                IsLoss: reader.GetBoolean(4),
                AttackerCount: reader.GetInt32(5),
                IsSolo: reader.GetBoolean(6),
                ShipTypeId: reader.GetNullableInt64(7),
                SystemId: reader.GetNullableInt64(8),
                LocationId: reader.GetNullableInt64(9),
                IsNpc: reader.GetBoolean(10),
                CachedAtUtc: FormatEngineTime(reader.GetUtcDateTimeOffset(11)));

            if (!grouped.TryGetValue(row.CharacterId, out var rows))
            {
                rows = new List<EngineKillmailRow>();
                grouped[row.CharacterId] = rows;
            }

            rows.Add(row);
        }

        return grouped;
    }

    private static Dictionary<long, EngineStatisticsRow> ReadStatistics(
        SqliteConnection connection,
        IReadOnlyList<long> characterIds)
    {
        var results = new Dictionary<long, EngineStatisticsRow>();

        using var command = connection.CreateCommand();
        command.CommandText = $"""
                               SELECT character_id, ships_destroyed, solo_kills, solo_ratio, avg_gang_size, ships_lost,
                                      solo_losses, general_style, checked_at_utc, no_history_marker, pod_losses
                               FROM main.zkill_statistics_cache
                               WHERE character_id IN ({IdList(characterIds)});
                               """;

        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            var row = new EngineStatisticsRow(
                CharacterId: reader.GetInt64(0),
                ShipsDestroyed: reader.GetInt32(1),
                SoloKills: reader.GetInt32(2),
                SoloRatio: reader.GetDouble(3),
                AvgGangSize: reader.GetDouble(4),
                ShipsLost: reader.GetInt32(5),
                SoloLosses: reader.GetInt32(6),
                GeneralStyle: reader.GetString(7),
                CheckedAtUtc: FormatEngineTime(reader.GetUtcDateTimeOffset(8)),
                NoHistoryMarker: reader.GetNullableBoolean(9) ?? false,
                PodLosses: reader.GetNullableInt32(10) ?? 0);

            results[row.CharacterId] = row;
        }

        return results;
    }

    private static List<EngineIdentityRow> ReadIdentities(
        SqliteConnection connection,
        IReadOnlyList<long> characterIds)
    {
        var results = new List<EngineIdentityRow>();

        if (characterIds.Count == 0)
            return results;

        using var command = connection.CreateCommand();
        command.CommandText = $"""
                               SELECT input_name, character_id, character_name, verify_status, security_status, corporation_id,
                                      corporation_name, corporation_ticker, alliance_id, alliance_name, alliance_ticker, cached_at_utc
                               FROM main.pilot_identity_cache
                               WHERE character_id IN ({IdList(characterIds)});
                               """;

        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            var cachedAtUtc = reader.GetUtcDateTimeOffset(11);

            results.Add(new EngineIdentityRow(
                InputName: reader.GetString(0),
                CharacterId: reader.GetNullableInt64(1),
                CharacterName: reader.GetNullableString(2),
                VerifyStatus: reader.GetString(3),
                SecurityStatus: reader.GetNullableDouble(4),
                CorporationId: reader.GetNullableInt64(5),
                CorporationName: reader.GetNullableString(6),
                CorporationTicker: reader.GetNullableString(7),
                AllianceId: reader.GetNullableInt64(8),
                AllianceName: reader.GetNullableString(9),
                AllianceTicker: reader.GetNullableString(10),
                CachedAtUtc: cachedAtUtc.ToString("yyyy-MM-dd'T'HH:mm:ss.FFFFFFFzzz", CultureInfo.InvariantCulture)));
        }

        return results;
    }

    private static Dictionary<long, EngineIdentityRow> FirstByCharacter(IEnumerable<EngineIdentityRow> rows)
    {
        var byCharacter = new Dictionary<long, EngineIdentityRow>();

        foreach (var row in rows)
        {
            if (row.CharacterId is { } characterId)
                byCharacter.TryAdd(characterId, row);
        }

        return byCharacter;
    }

    private static Dictionary<long, string> ReadCoverageStarts(
        SqliteConnection connection,
        IReadOnlyList<long> characterIds)
    {
        var results = new Dictionary<long, string>();

        using var command = connection.CreateCommand();
        command.CommandText = $"""
                               SELECT character_id, recent_coverage_start_utc
                               FROM main.zkill_activity_cache
                               WHERE character_id IN ({IdList(characterIds)});
                               """;

        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            if (reader.GetNullableDateTimeOffset(1) is { } coverageStartUtc)
                results[reader.GetInt64(0)] = FormatEngineTime(coverageStartUtc);
        }

        return results;
    }

    private static List<EngineAttackerEvidenceRow> ReadAttackerEvidence(
        SqliteConnection connection,
        IReadOnlyList<long> characterIds,
        bool qualifyingOnly)
    {
        var results = new List<EngineAttackerEvidenceRow>();

        if (characterIds.Count == 0)
            return results;

        var ids = IdList(characterIds);

        using var command = connection.CreateCommand();
        command.CommandText = qualifyingOnly
            ? $"""
               SELECT a.killmail_id, a.character_id, a.corporation_id, a.alliance_id, k.kill_time_utc, k.unique_attacker_count
               FROM main.zkill_killmail_attackers a
               JOIN main.zkill_killmails k ON k.killmail_id = a.killmail_id
               WHERE k.is_qualifying = 1
               AND a.killmail_id IN (
                   SELECT DISTINCT killmail_id FROM main.zkill_killmail_attackers WHERE character_id IN ({ids})
               )
               ORDER BY a.killmail_id, k.kill_time_utc;
               """
            : $"""
               SELECT a.killmail_id, a.character_id, a.corporation_id, a.alliance_id, k.kill_time_utc, k.unique_attacker_count
               FROM main.zkill_killmail_attackers a
               JOIN main.zkill_killmails k ON k.killmail_id = a.killmail_id
               WHERE a.character_id IN ({ids})
               ORDER BY a.killmail_id, k.kill_time_utc;
               """;

        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            results.Add(new EngineAttackerEvidenceRow(
                KillmailId: reader.GetInt64(0),
                CharacterId: reader.GetInt64(1),
                CorporationId: reader.GetNullableInt64(2),
                AllianceId: reader.GetNullableInt64(3),
                KillTimeUtc: FormatEngineTime(reader.GetUtcDateTimeOffset(4)),
                UniqueAttackerCount: reader.GetInt32(5)));
        }

        return results;
    }
}

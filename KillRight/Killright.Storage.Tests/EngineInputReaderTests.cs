using System.Text.Json;
using DuckDB.NET.Data;
using Killright.Storage.Database;
using Killright.Storage.Diagnostics;
using Killright.Storage.Engine;
using Killright.Storage.Sde;
using Xunit;

namespace Killright.Storage.Tests;

public sealed class EngineInputReaderTests
{
    private const long Lukas = 95465499;
    private const long Tral = 91321792;
    private const long Symptom = 2112625428;
    private const long Outsider = 90000003;

    [Fact]
    public async Task ReadPilotInputs_VictimAndAttackerKillmails_ReturnedNewestFirstPerPilot()
    {
        var fixture = CreateFixture();
        Execute(fixture.Database, $"""
            INSERT INTO main.zkill_killmails (killmail_id, killmail_hash, kill_time_utc, system_id, location_id, victim_character_id, victim_ship_type_id, unique_attacker_count, is_solo, is_npc, is_qualifying, cached_at_utc) VALUES
                (1, 'hash1', '2026-09-20T00:00:00+00:00', 30000142, 40000001, {Tral}, 587, 2, FALSE, FALSE, TRUE, '2026-09-20T00:00:00+00:00'),
                (2, 'hash2', '2026-09-22T00:00:00+00:00', 30000142, 40000001, {Lukas}, 670, 3, FALSE, FALSE, TRUE, '2026-09-22T00:00:00+00:00'),
                (3, 'hash3', '2026-09-21T00:00:00+00:00', 30000142, NULL, 777, 587, 1, TRUE, FALSE, FALSE, '2026-09-21T00:00:00+00:00');
            INSERT INTO main.zkill_killmail_attackers (killmail_id, character_id, corporation_id, alliance_id, ship_type_id) VALUES
                (1, {Lukas}, 98000001, NULL, 11567),
                (3, {Lukas}, 98000001, NULL, 11567);
            """);

        var results = await fixture.Reader.ReadPilotInputsAsync([Lukas]);

        var inputs = Assert.Single(results).Inputs!;
        Assert.Equal([2L, 3L, 1L], inputs.Killmails.Select(row => row.KillmailId).ToArray());
        Assert.Equal([true, false, false], inputs.Killmails.Select(row => row.IsLoss).ToArray());
        Assert.Equal(670, inputs.Killmails[0].ShipTypeId);
        Assert.Equal(587, inputs.Killmails[1].ShipTypeId);
        Assert.True(inputs.Killmails[1].IsSolo);
        Assert.Equal(1, inputs.Killmails[1].AttackerCount);
        Assert.Null(inputs.Killmails[1].LocationId);
        Assert.Equal("hash1", inputs.Killmails[2].KillmailHash);
        Assert.Equal(30000142, inputs.Killmails[2].SystemId);
        Assert.Equal(Lukas, inputs.Killmails[2].CharacterId);
    }

    [Fact]
    public async Task ReadPilotInputs_BatchMatchesSinglePilotReads()
    {
        var fixture = CreateFixture();
        SeedBatchData(fixture.Database);

        var batch = await fixture.Reader.ReadPilotInputsAsync([Lukas, Tral, Symptom]);
        var singles = new List<EnginePilotInputsResult>();

        foreach (var characterId in new[] { Lukas, Tral, Symptom })
            singles.Add(Assert.Single(await fixture.Reader.ReadPilotInputsAsync([characterId])));

        Assert.Equal(3, batch.Count);
        Assert.Equal(
            singles.Select(result => JsonSerializer.Serialize(result.Inputs)),
            batch.Select(result => JsonSerializer.Serialize(result.Inputs)));
        Assert.Equal([Lukas, Tral, Symptom], batch.Select(result => result.CharacterId).ToArray());
        Assert.All(batch, result => Assert.Null(result.FailureReason));
    }

    [Fact]
    public async Task ReadPilotInputs_StatisticsIdentityAndCoverageStart_ArePerPilot()
    {
        var fixture = CreateFixture();
        SeedBatchData(fixture.Database);

        var results = await fixture.Reader.ReadPilotInputsAsync([Lukas, Tral, 5]);

        var lukas = results[0].Inputs!;
        Assert.Equal(100, lukas.Statistics!.ShipsDestroyed);
        Assert.Equal(0.25, lukas.Statistics.SoloRatio);
        Assert.Equal("Gang", lukas.Statistics.GeneralStyle);
        Assert.False(lukas.Statistics.NoHistoryMarker);
        Assert.Equal(3, lukas.Statistics.PodLosses);
        Assert.Equal("Lukas Naarii", lukas.Identity!.CharacterName);
        Assert.Equal(98000001, lukas.Identity.CorporationId);
        Assert.Null(lukas.Identity.AllianceId);
        Assert.Equal(1.2, lukas.Identity.SecurityStatus);
        Assert.Equal("2026-09-20T00:00:00+00:00", lukas.Identity.CachedAtUtc);
        Assert.Equal("2026-09-08T00:00:00+00:00", lukas.CoverageStartUtc);

        var tral = results[1].Inputs!;
        Assert.Equal(0, tral.Statistics!.PodLosses);
        Assert.False(tral.Statistics.NoHistoryMarker);
        Assert.Equal(99000001, tral.Identity!.AllianceId);
        Assert.Null(tral.CoverageStartUtc);

        var unknown = results[2].Inputs!;
        Assert.Empty(unknown.Killmails);
        Assert.Null(unknown.Statistics);
        Assert.Null(unknown.Identity);
        Assert.Null(unknown.CoverageStartUtc);
    }

    [Fact]
    public async Task ReadPilotInputs_TwoIdentityRowsForOneCharacter_FirstRowIsUsed()
    {
        var fixture = CreateFixture();
        Execute(fixture.Database, $"""
            INSERT INTO main.pilot_identity_cache (input_name, character_id, character_name, verify_status, security_status, corporation_id, cached_at_utc) VALUES
                ('Lukas Naarii', {Lukas}, 'Lukas Naarii', 'Verified', 1.2, 98000001, '2026-09-20T00:00:00'),
                ('LUKAS NAARII', {Lukas}, 'Lukas Naarii', 'Verified', 1.2, 98000009, '2026-09-21T00:00:00');
            """);

        var results = await fixture.Reader.ReadPilotInputsAsync([Lukas]);

        Assert.Equal(98000001, Assert.Single(results).Inputs!.Identity!.CorporationId);
    }

    [Fact]
    public async Task ReadPilotInputs_EmptyIdList_ReturnsEmptyWithoutOpeningAConnection()
    {
        var fixture = CreateFixture();
        var before = fixture.Database.ConnectionsOpened;

        var results = await fixture.Reader.ReadPilotInputsAsync([]);

        Assert.Empty(results);
        Assert.Equal(before, fixture.Database.ConnectionsOpened);
    }

    [Fact]
    public async Task ReadGroupInputs_EmptyScanSet_ReturnsEmptyEvidence()
    {
        var fixture = CreateFixture();

        var result = await fixture.Reader.ReadGroupInputsAsync([]);

        Assert.Null(result.FailureReason);
        Assert.Empty(result.Inputs!.DirectEvidence);
        Assert.Empty(result.Inputs.ChainEvidence);
        Assert.Empty(result.Inputs.Identities);
    }

    [Fact]
    public async Task ReadGroupInputs_DirectEvidence_ScanSetOnlyOrderedByKillmailThenTime()
    {
        var fixture = CreateFixture();
        Execute(fixture.Database, $"""
            INSERT INTO main.zkill_killmails (killmail_id, killmail_hash, kill_time_utc, system_id, location_id, victim_character_id, victim_ship_type_id, unique_attacker_count, is_solo, is_npc, is_qualifying, cached_at_utc) VALUES
                (7, 'h7', '2026-09-21T00:00:00+00:00', 30000142, NULL, 999, 587, 3, FALSE, FALSE, TRUE, '2026-09-21T00:00:00+00:00'),
                (5, 'h5', '2026-09-20T00:00:00+00:00', 30000142, NULL, 999, 587, 2, FALSE, FALSE, TRUE, '2026-09-20T00:00:00+00:00');
            INSERT INTO main.zkill_killmail_attackers (killmail_id, character_id, corporation_id, alliance_id, ship_type_id) VALUES
                (7, {Lukas}, 98000001, NULL, 11567),
                (7, {Tral}, 98000002, 99000001, 17738),
                (7, {Outsider}, 98000003, NULL, 670),
                (5, {Lukas}, 98000001, NULL, 11567);
            """);

        var result = await fixture.Reader.ReadGroupInputsAsync([Lukas, Tral]);

        var evidence = result.Inputs!.DirectEvidence;
        Assert.Equal([5L, 7L, 7L], evidence.Select(row => row.KillmailId).ToArray());
        Assert.DoesNotContain(evidence, row => row.CharacterId == Outsider);
        Assert.Equal(99000001, evidence.Single(row => row.CharacterId == Tral).AllianceId);
        Assert.Equal(3L, evidence.First(row => row.KillmailId == 7).UniqueAttackerCount);
        Assert.Equal("2026-09-20T00:00:00+00:00", evidence[0].KillTimeUtc);
    }

    [Fact]
    public async Task ReadGroupInputs_ChainEvidence_IncludesEveryAttackerOnQualifyingKillmailsTouchingTheScanSet()
    {
        var fixture = CreateFixture();
        Execute(fixture.Database, $"""
            INSERT INTO main.zkill_killmails (killmail_id, killmail_hash, kill_time_utc, system_id, location_id, victim_character_id, victim_ship_type_id, unique_attacker_count, is_solo, is_npc, is_qualifying, cached_at_utc) VALUES
                (1, 'h1', '2026-09-20T00:00:00+00:00', 30000142, NULL, 999, 587, 2, FALSE, FALSE, TRUE, '2026-09-20T00:00:00+00:00'),
                (2, 'h2', '2026-09-20T00:00:00+00:00', 30000142, NULL, 999, 587, 1, TRUE, FALSE, FALSE, '2026-09-20T00:00:00+00:00'),
                (3, 'h3', '2026-09-20T00:00:00+00:00', 30000142, NULL, 999, 587, 2, FALSE, FALSE, TRUE, '2026-09-20T00:00:00+00:00');
            INSERT INTO main.zkill_killmail_attackers (killmail_id, character_id, corporation_id, alliance_id, ship_type_id) VALUES
                (1, {Lukas}, 98000001, NULL, 11567),
                (1, {Outsider}, 98000003, NULL, 670),
                (2, {Lukas}, 98000001, NULL, 11567),
                (3, {Tral}, 98000002, NULL, 11567),
                (3, {Symptom}, 98000004, NULL, 11567);
            """);

        var result = await fixture.Reader.ReadGroupInputsAsync([Lukas]);

        var evidence = result.Inputs!.ChainEvidence;
        Assert.Equal(2, evidence.Count);
        Assert.Contains(evidence, row => row.CharacterId == Lukas && row.KillmailId == 1);
        Assert.Contains(evidence, row => row.CharacterId == Outsider && row.KillmailId == 1);
        Assert.DoesNotContain(evidence, row => row.KillmailId is 2 or 3);
    }

    [Fact]
    public async Task ReadGroupInputs_IdentitiesAndNpcCorporations_ComeFromTheScanSetAndTheReferenceStore()
    {
        var fixture = CreateFixture();
        SeedBatchData(fixture.Database);
        Execute(fixture.Database, "INSERT INTO main.sde_npc_corporations (corporation_id) VALUES (1000001), (1000132);");

        var result = await fixture.Reader.ReadGroupInputsAsync([Lukas, Tral]);

        var inputs = result.Inputs!;
        Assert.Equal(
            new[] { Lukas, Tral }.Order().ToArray(),
            inputs.Identities.Select(row => row.CharacterId!.Value).Order().ToArray());
        Assert.Equal([1000001L, 1000132L], inputs.NpcCorporationIds.Order().ToArray());
    }

    [Theory]
    [InlineData("zkill_killmails", EngineInputFailureReasons.Killmails)]
    [InlineData("zkill_statistics_cache", EngineInputFailureReasons.Statistics)]
    [InlineData("pilot_identity_cache", EngineInputFailureReasons.Identity)]
    [InlineData("zkill_activity_cache", EngineInputFailureReasons.ActivityCache)]
    public async Task ReadPilotInputs_MissingTable_FailsEveryPilotNamingTheDataSet(string table, string expectedReason)
    {
        var messages = new List<string>();
        var fixture = CreateFixture(messages.Add);
        Execute(fixture.Database, $"DROP TABLE main.{table};");

        var results = await fixture.Reader.ReadPilotInputsAsync([Lukas, Tral]);

        Assert.Equal(2, results.Count);
        Assert.All(results, result =>
        {
            Assert.Null(result.Inputs);
            Assert.Equal(expectedReason, result.FailureReason);
        });
        Assert.Contains(messages, message => message.Contains(expectedReason, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("zkill_killmail_attackers", EngineInputFailureReasons.DirectEvidence)]
    [InlineData("pilot_identity_cache", EngineInputFailureReasons.Identity)]
    public async Task ReadGroupInputs_MissingTable_FailsNamingTheDataSet(string table, string expectedReason)
    {
        var fixture = CreateFixture();
        Execute(fixture.Database, $"DROP TABLE main.{table};");

        var result = await fixture.Reader.ReadGroupInputsAsync([Lukas, Tral]);

        Assert.Null(result.Inputs);
        Assert.Equal(expectedReason, result.FailureReason);
    }

    [Fact]
    public async Task ReadPilotInputs_WithTiming_RecordsTheReadPhasesAtEngineLevel()
    {
        var fixture = CreateFixture();
        var timings = new ScanTimings();

        await fixture.Reader.ReadPilotInputsAsync([Lukas], timings);
        await fixture.Reader.ReadGroupInputsAsync([Lukas], timings);

        var phases = timings.Rows.Where(row => row.Level == ScanTimings.EngineLevel).Select(row => row.Phase).ToHashSet();
        Assert.Superset(
            new HashSet<string>
            {
                "engine_input_read", "killmails_read", "statistics_read", "identity_read", "activity_cache_read",
                "group_input_read", "direct_evidence_read", "chain_evidence_read", "identities_read"
            },
            phases);
    }

    private static Fixture CreateFixture(Action<string>? log = null)
    {
        var path = Path.Combine(Path.GetTempPath(), $"engineInput.{Guid.NewGuid():N}.duckdb");
        var database = new KillRightDatabase(new KillRightDatabaseOptions { DatabasePath = path });
        database.EnsureCreated();

        return new Fixture(database, new EngineInputReader(database, new DuckDbSdeReferenceDataStore(database), log));
    }

    private static void SeedBatchData(KillRightDatabase database)
    {
        Execute(database, $"""
            INSERT INTO main.zkill_killmails (killmail_id, killmail_hash, kill_time_utc, system_id, location_id, victim_character_id, victim_ship_type_id, unique_attacker_count, is_solo, is_npc, is_qualifying, cached_at_utc) VALUES
                (1, 'hash1', '2026-09-20T00:00:00+00:00', 30000142, 40000001, {Tral}, 587, 2, FALSE, FALSE, TRUE, '2026-09-20T00:00:00+00:00'),
                (2, 'hash2', '2026-09-21T00:00:00+00:00', 30000142, 40000001, {Lukas}, 670, 3, FALSE, FALSE, TRUE, '2026-09-21T00:00:00+00:00'),
                (3, 'hash3', '2026-09-22T00:00:00+00:00', 30000142, 40000001, 777, 587, 1, TRUE, FALSE, FALSE, '2026-09-22T00:00:00+00:00');
            INSERT INTO main.zkill_killmail_attackers (killmail_id, character_id, corporation_id, alliance_id, ship_type_id) VALUES
                (1, {Lukas}, 98000001, NULL, 11567),
                (3, {Symptom}, 98000002, NULL, 11567);
            INSERT INTO main.zkill_statistics_cache (character_id, ships_destroyed, solo_kills, solo_ratio, avg_gang_size, ships_lost, solo_losses, general_style, checked_at_utc, no_history_marker, pod_losses) VALUES
                ({Lukas}, 100, 20, 0.25, 4.5, 10, 1, 'Gang', '2026-09-20T00:00:00+00:00', FALSE, 3);
            INSERT INTO main.zkill_statistics_cache (character_id, ships_destroyed, solo_kills, solo_ratio, avg_gang_size, ships_lost, solo_losses, general_style, checked_at_utc) VALUES
                ({Tral}, 5, 0, 0.0, 6.0, 1, 0, 'Fleet', '2026-09-20T00:00:00+00:00');
            INSERT INTO main.pilot_identity_cache (input_name, character_id, character_name, verify_status, security_status, corporation_id, corporation_name, corporation_ticker, alliance_id, alliance_name, alliance_ticker, cached_at_utc) VALUES
                ('Lukas Naarii', {Lukas}, 'Lukas Naarii', 'Verified', 1.2, 98000001, 'Corp One', 'ONE', NULL, NULL, NULL, '2026-09-20T00:00:00'),
                ('T''ral Vsengne', {Tral}, 'T''ral Vsengne', 'Verified', 0.4, 98000002, 'Corp Two', 'TWO', 99000001, 'Alliance Two', 'ATWO', '2026-09-20T00:00:00');
            INSERT INTO main.zkill_activity_cache (character_id, has_public_activity_data, checked_at_utc, recent_coverage_start_utc) VALUES
                ({Lukas}, TRUE, '2026-09-22T00:00:00+00:00', '2026-09-08T00:00:00+00:00'),
                ({Tral}, TRUE, '2026-09-22T00:00:00+00:00', NULL);
            """);
    }

    private static void Execute(KillRightDatabase database, string sql)
    {
        using var connection = new DuckDBConnection(database.ConnectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private sealed record Fixture(KillRightDatabase Database, EngineInputReader Reader);
}

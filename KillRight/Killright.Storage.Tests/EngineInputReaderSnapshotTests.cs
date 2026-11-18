using Killright.Storage.Database;
using Killright.Storage.Engine;
using Killright.Storage.Sde;
using Xunit;

namespace Killright.Storage.Tests;

public sealed class EngineInputReaderSnapshotTests
{
    private const long Lukas = 95465499;

    [Fact]
    public async Task ReadPilotInputs_WriteCommittedBetweenReads_IsInNoneOfTheReads()
    {
        var database = CreateDatabase();
        var reader = new EngineInputReader(database, new SdeReferenceDataStore(database))
        {
            AfterRead = phase =>
            {
                if (phase == "killmails_read")
                    CommitLukasRows(database);
            }
        };

        var results = await reader.ReadPilotInputsAsync([Lukas]);

        var inputs = Assert.Single(results).Inputs!;
        Assert.Empty(inputs.Killmails);
        Assert.Null(inputs.Statistics);
        Assert.Null(inputs.Identity);
        Assert.Null(inputs.CoverageStartUtc);
    }

    [Fact]
    public async Task ReadGroupInputs_WriteCommittedBetweenReads_IsInNoneOfTheReads()
    {
        var database = CreateDatabase();
        var reader = new EngineInputReader(database, new SdeReferenceDataStore(database))
        {
            AfterRead = phase =>
            {
                if (phase == "direct_evidence_read")
                    CommitLukasRows(database);
            }
        };

        var result = await reader.ReadGroupInputsAsync([Lukas]);

        var inputs = result.Inputs!;
        Assert.Empty(inputs.DirectEvidence);
        Assert.Empty(inputs.ChainEvidence);
        Assert.Empty(inputs.Identities);
    }

    [Fact]
    public async Task ReadPilotInputs_AfterTheReads_ASubsequentCallSeesTheCommittedWrite()
    {
        var database = CreateDatabase();
        var reader = new EngineInputReader(database, new SdeReferenceDataStore(database));
        CommitLukasRows(database);

        var results = await reader.ReadPilotInputsAsync([Lukas]);

        var inputs = Assert.Single(results).Inputs!;
        Assert.Equal(2, inputs.Killmails.Count);
        Assert.NotNull(inputs.Statistics);
        Assert.NotNull(inputs.Identity);
    }

    private static KillRightDatabase CreateDatabase()
    {
        var path = Path.Combine(Path.GetTempPath(), $"engineSnapshot.{Guid.NewGuid():N}.db");
        var database = new KillRightDatabase(new KillRightDatabaseOptions { DatabasePath = path });
        database.EnsureCreated();

        return database;
    }

    private static void CommitLukasRows(KillRightDatabase database)
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = $"""
            INSERT INTO main.zkill_killmails (killmail_id, killmail_hash, kill_time_utc, system_id, location_id, victim_character_id, victim_ship_type_id, unique_attacker_count, is_solo, is_npc, is_qualifying, cached_at_utc) VALUES
                (1, 'hash1', 1789862400, 30000142, 40000001, {Lukas}, 587, 2, 0, 0, 1, 1789862400);
            INSERT INTO main.zkill_killmail_attackers (killmail_id, character_id, corporation_id, alliance_id, ship_type_id) VALUES
                (1, {Lukas}, 98000001, NULL, 11567);
            INSERT INTO main.zkill_statistics_cache (character_id, ships_destroyed, solo_kills, solo_ratio, avg_gang_size, ships_lost, solo_losses, general_style, checked_at_utc) VALUES
                ({Lukas}, 100, 20, 0.25, 4.5, 10, 1, 'gang', 1789862400);
            INSERT INTO main.pilot_identity_cache (input_name, character_id, character_name, verify_status, security_status, corporation_id, corporation_name, corporation_ticker, alliance_id, alliance_name, alliance_ticker, cached_at_utc) VALUES
                ('Lukas Naarii', {Lukas}, 'Lukas Naarii', 'Verified', 1.2, 98000001, 'Corp One', 'ONE', NULL, NULL, NULL, 1789862400);
            INSERT INTO main.zkill_activity_cache (character_id, has_public_activity_data, checked_at_utc, recent_coverage_start_utc) VALUES
                ({Lukas}, 1, 1790035200, 1788825600);
            """;
        command.ExecuteNonQuery();
    }
}

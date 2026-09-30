using Killright.Core.Models;
using Killright.Shared;
using Killright.Shared.Killmails;
using Killright.Storage.Database;
using Killright.Storage.Identity;
using Killright.Storage.Scan;
using Killright.UI.Analysis;
using Xunit;

namespace Killright.UI.Tests.Analysis;

public sealed class EngineBatchIntegrationTests
{
    private const long Lukas = 95465499;
    private const long Tral = 91321792;

    [Fact]
    public async Task AnalyzePilotsAsync_AgainstTheRealEngine_ReturnsDerivedViewsAndReleasesTheDatabaseFile()
    {
        var dllPath = FindEngineDll();

        if (dllPath is null)
            return;

        var databasePath = Path.Combine(Path.GetTempPath(), $"engineBatch.{Guid.NewGuid():N}.duckdb");
        var database = new KillRightDatabase(new KillRightDatabaseOptions { DatabasePath = databasePath });
        database.EnsureCreated();

        var now = DateTimeOffset.UtcNow;
        var batch = new ScanWriteBatch();
        batch.AddIdentity(new PilotIdentityCacheRecord
        {
            InputName = "LUKAS NAARII",
            CharacterId = Lukas,
            CharacterName = "Lukas Naarii",
            VerifyStatus = VerifyStatus.Partial,
            SecurityStatus = 1.5,
            CorporationId = 98000001,
            SecurityStatusAtUtc = now.UtcDateTime,
            CachedAtUtc = now.UtcDateTime
        });
        batch.AddKillmails(0, Lukas,
        [
            new RawKillmail(
                910001, "hash1", now.AddDays(-1), 30000142, null, 999, 587, false, false,
                [new KillmailAttacker(Lukas, 98000001, null, 11567), new KillmailAttacker(Tral, 98000002, null, 17738)]),
            new RawKillmail(
                910002, "hash2", now.AddDays(-2), 30000142, null, 998, 670, true, false,
                [new KillmailAttacker(Lukas, 98000001, null, 11567)])
        ]);

        using (var session = database.OpenScanSession())
            ScanWriter.Commit(session, batch, 11, now);

        var settingsPath = FindFile("KillRight/Killright.UI/config/settings.json")!;

        using var runtime = new KillrightEngineRuntime(dllPath, databasePath, settingsPath);
        Assert.True(runtime.IsAvailable);

        var results = await new RustRecentStyleClient(runtime).AnalyzePilotsAsync([Lukas, Tral]);

        Assert.Equal(2, results.Count);
        Assert.Null(results[0].FailureReason);
        Assert.Null(results[1].FailureReason);
        Assert.True(results[0].DerivedActivity!.HasPublicActivityData);
        Assert.Equal(1, results[0].DerivedActivity!.KillsWeek);
        Assert.Equal(Killright.Shared.zKill.zKillActivityType.Kill, results[0].DerivedActivity!.NewestKillActivityType);
        Assert.Contains(results[1].DerivedActivity!.HasPublicActivityData ? "data" : "none", new[] { "data", "none" });

        var after = new ScanWriteBatch();
        after.AddActivity(new Killright.Integration.zKill.zKillActivity(Lukas, true, 1, 0, now, Killright.Shared.zKill.zKillActivityType.Kill, now));

        using (var session = database.OpenScanSession())
            ScanWriter.Commit(session, after, 11, now);
    }

    private static string? FindEngineDll()
    {
        foreach (var relative in new[] { "killright_engine/target/release/killright_engine.dll", "killright_engine/target/debug/killright_engine.dll" })
        {
            var found = FindFile(relative);

            if (found is not null)
                return found;
        }

        return null;
    }

    private static string? FindFile(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, relativePath);

            if (File.Exists(candidate))
                return candidate;

            directory = directory.Parent;
        }

        return null;
    }
}

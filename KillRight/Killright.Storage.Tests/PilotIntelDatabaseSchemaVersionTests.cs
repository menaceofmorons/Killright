using Killright.Storage.Database;
using Xunit;

namespace Killright.Storage.Tests;

public sealed class PilotIntelDatabaseSchemaVersionTests
{
    [Fact]
    public void EnsureCreated_NewDatabase_StoresCurrentSchemaVersion()
    {
        var database = CreateDatabase();

        database.EnsureCreated();

        Assert.Equal(KillRightDatabase.CurrentSchemaVersion, database.GetSchemaVersion());
    }

    [Fact]
    public void EnsureCreated_CalledTwice_DoesNotDuplicateSchemaMetadataRow()
    {
        var database = CreateDatabase();

        database.EnsureCreated();
        database.EnsureCreated();

        Assert.Equal(KillRightDatabase.CurrentSchemaVersion, database.GetSchemaVersion());
    }

    [Fact]
    public void EnsureCreatedWithRecovery_ValidDatabase_DoesNotReportRecovery()
    {
        var database = CreateDatabase();

        var wasRecovered = database.EnsureCreatedWithRecovery();

        Assert.False(wasRecovered);
        Assert.Equal(KillRightDatabase.CurrentSchemaVersion, database.GetSchemaVersion());
    }

    [Fact]
    public void EnsureCreatedWithRecovery_CorruptDatabaseFile_QuarantinesAndRebuilds()
    {
        var path = Path.Combine(Path.GetTempPath(), $"schemaVersion.{Guid.NewGuid():N}.duckdb");
        File.WriteAllBytes(path, [0x00, 0x01, 0x02, 0x03]);

        var database = new KillRightDatabase(new KillRightDatabaseOptions { DatabasePath = path });

        string? reportedReason = null;
        var wasRecovered = database.EnsureCreatedWithRecovery(reason => reportedReason = reason);

        Assert.True(wasRecovered);
        Assert.NotNull(reportedReason);
        Assert.Equal(KillRightDatabase.CurrentSchemaVersion, database.GetSchemaVersion());
        Assert.Contains(Directory.GetFiles(Path.GetTempPath(), $"{Path.GetFileName(path)}.corrupt-*"), _ => true);
    }

    private static KillRightDatabase CreateDatabase()
    {
        var path = Path.Combine(Path.GetTempPath(), $"schemaVersion.{Guid.NewGuid():N}.duckdb");
        return new KillRightDatabase(new KillRightDatabaseOptions { DatabasePath = path });
    }
}

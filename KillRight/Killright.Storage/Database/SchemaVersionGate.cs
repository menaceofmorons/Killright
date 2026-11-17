using Microsoft.Data.Sqlite;

namespace Killright.Storage.Database;

public enum SchemaVersionCheckOutcome
{
    Ok,
    RefusedNoMigrationPath,
    RefusedDowngrade
}

public sealed record SchemaVersionCheckResult(SchemaVersionCheckOutcome Outcome, int StoredVersion, int CurrentVersion);

public sealed record SchemaMigration(int FromVersion, int ToVersion, Action<SqliteConnection, SqliteTransaction> Apply);

public static class SchemaVersionGate
{
    public static readonly IReadOnlyList<SchemaMigration> Migrations = [];

    public static SchemaVersionCheckResult CheckOnStartup(
        KillRightDatabase database,
        bool isReleaseBuild,
        IReadOnlyList<SchemaMigration>? migrations = null)
    {
        var available = migrations ?? Migrations;
        var storedVersion = database.GetSchemaVersion();
        var currentVersion = KillRightDatabase.CurrentSchemaVersion;
        var locked = database.GetAlphaLock();

        if (storedVersion != currentVersion)
        {
            if (!locked)
                database.RebuildKillmailAndAttackerTables();
            else if (storedVersion > currentVersion)
                return new SchemaVersionCheckResult(SchemaVersionCheckOutcome.RefusedDowngrade, storedVersion, currentVersion);
            else
            {
                var migrationPath = FindMigrationPath(available, storedVersion, currentVersion);

                if (migrationPath is null)
                    return new SchemaVersionCheckResult(SchemaVersionCheckOutcome.RefusedNoMigrationPath, storedVersion, currentVersion);

                foreach (var migration in migrationPath)
                    database.ApplyMigration(migration.Apply, migration.ToVersion);
            }

            if (!locked)
                database.SetSchemaVersion(currentVersion);
        }

        if (isReleaseBuild && !locked)
            database.SetAlphaLock();

        return new SchemaVersionCheckResult(SchemaVersionCheckOutcome.Ok, storedVersion, currentVersion);
    }

    private static IReadOnlyList<SchemaMigration>? FindMigrationPath(IReadOnlyList<SchemaMigration> migrations, int fromVersion, int toVersion)
    {
        var path = new List<SchemaMigration>();
        var current = fromVersion;

        while (current < toVersion)
        {
            var next = migrations.FirstOrDefault(migration => migration.FromVersion == current);

            if (next is null)
                return null;

            path.Add(next);
            current = next.ToVersion;
        }

        return path;
    }
}

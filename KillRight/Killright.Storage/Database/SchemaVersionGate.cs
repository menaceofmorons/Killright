namespace Killright.Storage.Database;

public enum SchemaVersionCheckOutcome
{
    Ok,
    RefusedNoMigrationPath,
    RefusedDowngrade
}

public sealed record SchemaVersionCheckResult(SchemaVersionCheckOutcome Outcome, int StoredVersion, int CurrentVersion);

public sealed record SchemaMigration(int FromVersion, int ToVersion, Action<KillRightDatabase> Apply);

public static class SchemaVersionGate
{
    public static readonly IReadOnlyList<SchemaMigration> Migrations =
    [
        new SchemaMigration(2, 3, database => database.EnsurePilotLastKillmailCache())
    ];

    public static SchemaVersionCheckResult CheckOnStartup(KillRightDatabase database, bool isAlphaRelease)
    {
        if (isAlphaRelease && !database.GetAlphaLock())
            database.SetAlphaLock();

        var locked = database.GetAlphaLock();
        var storedVersion = database.GetSchemaVersion();
        var currentVersion = KillRightDatabase.CurrentSchemaVersion;

        if (storedVersion == currentVersion)
            return new SchemaVersionCheckResult(SchemaVersionCheckOutcome.Ok, storedVersion, currentVersion);

        if (!locked)
        {
            database.RebuildKillmailAndAttackerTables();
            database.SetSchemaVersion(currentVersion);
            return new SchemaVersionCheckResult(SchemaVersionCheckOutcome.Ok, storedVersion, currentVersion);
        }

        if (storedVersion > currentVersion)
            return new SchemaVersionCheckResult(SchemaVersionCheckOutcome.RefusedDowngrade, storedVersion, currentVersion);

        var migrationPath = FindMigrationPath(storedVersion, currentVersion);

        if (migrationPath is null)
            return new SchemaVersionCheckResult(SchemaVersionCheckOutcome.RefusedNoMigrationPath, storedVersion, currentVersion);

        foreach (var migration in migrationPath)
            migration.Apply(database);

        database.SetSchemaVersion(currentVersion);

        return new SchemaVersionCheckResult(SchemaVersionCheckOutcome.Ok, storedVersion, currentVersion);
    }

    private static IReadOnlyList<SchemaMigration>? FindMigrationPath(int fromVersion, int toVersion)
    {
        var path = new List<SchemaMigration>();
        var current = fromVersion;

        while (current < toVersion)
        {
            var next = Migrations.FirstOrDefault(migration => migration.FromVersion == current);

            if (next is null)
                return null;

            path.Add(next);
            current = next.ToVersion;
        }

        return path;
    }
}

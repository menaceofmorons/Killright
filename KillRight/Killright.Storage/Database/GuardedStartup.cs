using Killright.Storage.Killmails;
using Microsoft.Data.Sqlite;

namespace Killright.Storage.Database;

public enum StartupOutcome
{
    Ready,
    DatabaseRefused,
    SchemaRefused
}

public sealed record StartupResult(StartupOutcome Outcome, SchemaVersionCheckResult? Schema = null);

internal sealed record StartupSteps(
    Func<Action<string>, bool> QuarantineIfCorrupt,
    Func<bool> DatabaseExists,
    Func<bool> TryRestore,
    Action SetAside,
    Action EnsureCreatedAndOpen,
    Func<SchemaVersionCheckResult> CheckSchema,
    Action Requalify);

public static class GuardedStartup
{
    private const int SqliteBusy = 5;
    private const int SqliteLocked = 6;

    public static StartupResult Run(
        KillRightDatabase database,
        IDatabaseBackupService backupService,
        bool isAlphaRelease,
        int qualificationFleetThreshold,
        Action<string> record)
    {
        return Run(
            new StartupSteps(
                database.QuarantineIfCorrupt,
                () => File.Exists(database.DatabasePath),
                backupService.TryRestore,
                database.SetAsideFiles,
                () =>
                {
                    database.EnsureCreated();
                    database.Open();
                },
                () => SchemaVersionGate.CheckOnStartup(database, isAlphaRelease),
                () => KillmailQualificationRequalifier.RequalifyOnStartup(database, qualificationFleetThreshold)),
            record);
    }

    internal static StartupResult Run(StartupSteps steps, Action<string> record)
    {
        try
        {
            steps.QuarantineIfCorrupt(reason =>
                record($"Operational database was corrupt at startup and has been set aside. {reason}"));

            RestoreWhenMissing(steps, record);
            OpenWithRestoreFallback(steps, record);

            var schema = steps.CheckSchema();

            if (schema.Outcome != SchemaVersionCheckOutcome.Ok)
                return new StartupResult(StartupOutcome.SchemaRefused, schema);

            steps.Requalify();

            return new StartupResult(StartupOutcome.Ready);
        }
        catch (Exception exception)
        {
            record($"Startup refused: the operational database could not be opened. {exception.Message}");

            return new StartupResult(StartupOutcome.DatabaseRefused);
        }
    }

    private static void RestoreWhenMissing(StartupSteps steps, Action<string> record)
    {
        if (!steps.DatabaseExists() && steps.TryRestore())
            record("Restored the database from the latest backup.");
    }

    private static void OpenWithRestoreFallback(StartupSteps steps, Action<string> record)
    {
        try
        {
            steps.EnsureCreatedAndOpen();
        }
        catch (Exception exception) when (IsRestorableOpenFailure(exception))
        {
            record($"The operational database could not be opened and has been set aside. {exception.Message}");

            steps.SetAside();
            RestoreWhenMissing(steps, record);
            steps.EnsureCreatedAndOpen();
        }
    }

    private static bool IsRestorableOpenFailure(Exception exception)
    {
        return exception switch
        {
            SqliteException sqlite => sqlite.SqliteErrorCode is not (SqliteBusy or SqliteLocked),
            InvalidOperationException => true,
            _ => false
        };
    }
}

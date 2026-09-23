using DuckDB.NET.Data;
using Killright.Shared.Data;
using Killright.Storage.Database;

namespace Killright.Storage.Killmails;

public sealed class DuckDbKillmailBackupService : IKillmailBackupService
{
    private const string KillmailsFileName = "zkill_killmails.parquet";
    private const string AttackersFileName = "zkill_killmail_attackers.parquet";
    private const string FingerprintFileName = "fingerprint.txt";
    private const string BackupFolderPattern = "????????T??????Z";

    private readonly KillRightDatabase _database;
    private readonly string _backupFolder;
    private readonly int _rotationCount;

    public DuckDbKillmailBackupService(KillRightDatabase database, string backupFolder, int rotationCount)
    {
        _database = database;
        _backupFolder = backupFolder;
        _rotationCount = rotationCount;
    }

    public Task BackupAsync(CancellationToken cancellationToken = default)
    {
        string currentFingerprint;

        using (var connection = new DuckDBConnection(_database.ConnectionString))
        {
            connection.Open();
            currentFingerprint = ComputeFingerprint(connection);
        }

        var latestFolder = GetBackupFolders().FirstOrDefault();

        if (latestFolder is not null && ReadFingerprint(latestFolder) == currentFingerprint)
            return Task.CompletedTask;

        Directory.CreateDirectory(_backupFolder);

        var stagingFolder = Path.Combine(_backupFolder, $".staging-{Guid.NewGuid():N}");
        Directory.CreateDirectory(stagingFolder);

        var killmailsPath = Path.Combine(stagingFolder, KillmailsFileName);
        var attackersPath = Path.Combine(stagingFolder, AttackersFileName);

        using (var connection = new DuckDBConnection(_database.ConnectionString))
        {
            connection.Open();

            using (var command = connection.CreateCommand())
            {
                command.CommandText = $"COPY (SELECT * FROM main.zkill_killmails) TO {SqlValueFormatter.String(killmailsPath)} (FORMAT PARQUET);";
                command.ExecuteNonQuery();
            }

            using (var command = connection.CreateCommand())
            {
                command.CommandText = $"COPY (SELECT * FROM main.zkill_killmail_attackers) TO {SqlValueFormatter.String(attackersPath)} (FORMAT PARQUET);";
                command.ExecuteNonQuery();
            }
        }

        File.WriteAllText(Path.Combine(stagingFolder, FingerprintFileName), currentFingerprint);

        var finalFolder = Path.Combine(_backupFolder, DateTimeOffset.UtcNow.ToString("yyyyMMdd'T'HHmmss'Z'"));

        if (Directory.Exists(finalFolder))
            Directory.Delete(finalFolder, recursive: true);

        Directory.Move(stagingFolder, finalFolder);

        RotateOldBackups();

        return Task.CompletedTask;
    }

    public Task<bool> TryRestoreAsync(CancellationToken cancellationToken = default)
    {
        var latestFolder = GetBackupFolders().FirstOrDefault();

        if (latestFolder is null)
            return Task.FromResult(false);

        var killmailsPath = Path.Combine(latestFolder, KillmailsFileName);
        var attackersPath = Path.Combine(latestFolder, AttackersFileName);

        if (!File.Exists(killmailsPath) || !File.Exists(attackersPath))
            return Task.FromResult(false);

        using var connection = new DuckDBConnection(_database.ConnectionString);
        connection.Open();

        using (var command = connection.CreateCommand())
        {
            command.CommandText = $"""
                                  INSERT INTO main.zkill_killmails
                                  SELECT * FROM read_parquet({SqlValueFormatter.String(killmailsPath)});
                                  """;
            command.ExecuteNonQuery();
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText = $"""
                                  INSERT INTO main.zkill_killmail_attackers
                                  SELECT * FROM read_parquet({SqlValueFormatter.String(attackersPath)});
                                  """;
            command.ExecuteNonQuery();
        }

        return Task.FromResult(true);
    }

    private void RotateOldBackups()
    {
        foreach (var folder in GetBackupFolders().Skip(_rotationCount))
        {
            try
            {
                Directory.Delete(folder, recursive: true);
            }
            catch
            {
                // A missed rotation deletion is not fatal -- the previous copy simply stands.
            }
        }
    }

    private List<string> GetBackupFolders()
    {
        if (!Directory.Exists(_backupFolder))
            return new List<string>();

        return Directory.GetDirectories(_backupFolder, BackupFolderPattern)
            .OrderByDescending(Path.GetFileName)
            .ToList();
    }

    private static string? ReadFingerprint(string folder)
    {
        var path = Path.Combine(folder, FingerprintFileName);
        return File.Exists(path) ? File.ReadAllText(path) : null;
    }

    private static string ComputeFingerprint(DuckDBConnection connection)
    {
        var killmailCount = ExecuteScalarLong(connection, "SELECT COUNT(*) FROM main.zkill_killmails;");
        var latestCachedAtUtc = ExecuteScalarString(connection, "SELECT COALESCE(MAX(cached_at_utc), '') FROM main.zkill_killmails;");
        var attackerCount = ExecuteScalarLong(connection, "SELECT COUNT(*) FROM main.zkill_killmail_attackers;");

        return $"{killmailCount}|{latestCachedAtUtc}|{attackerCount}";
    }

    private static long ExecuteScalarLong(DuckDBConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(command.ExecuteScalar());
    }

    private static string ExecuteScalarString(DuckDBConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar() as string ?? "";
    }
}

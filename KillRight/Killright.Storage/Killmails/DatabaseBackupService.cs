using Microsoft.Data.Sqlite;
using Killright.Storage.Database;

namespace Killright.Storage.Killmails;

public sealed class DatabaseBackupService : IDatabaseBackupService
{
    private const string BackupFilePattern = "KillRight-*.db";
    private const string BackupFilePrefix = "KillRight-";
    private const string BackupFileExtension = ".db";
    private const string FingerprintExtension = ".fingerprint";
    private const string TemporaryExtension = ".tmp";

    private readonly KillRightDatabase _database;
    private readonly string _backupFolder;
    private readonly int _rotationCount;
    private readonly Func<DateTimeOffset> _utcNow;

    public DatabaseBackupService(
        KillRightDatabase database,
        string backupFolder,
        int rotationCount,
        Func<DateTimeOffset>? utcNow = null)
    {
        _database = database;
        _backupFolder = backupFolder;
        _rotationCount = rotationCount;
        _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
    }

    public Task BackupAsync(CancellationToken cancellationToken = default)
    {
        string currentFingerprint;

        using (var connection = _database.OpenConnection())
            currentFingerprint = ComputeFingerprint(connection);

        var latestBackup = GetBackupFiles().FirstOrDefault();

        if (latestBackup is not null && ReadFingerprint(latestBackup) == currentFingerprint)
            return Task.CompletedTask;

        Directory.CreateDirectory(_backupFolder);

        var finalPath = Path.Combine(
            _backupFolder,
            $"{BackupFilePrefix}{_utcNow().UtcDateTime:yyyyMMdd'T'HHmmss'Z'}{BackupFileExtension}");
        var temporaryPath = finalPath + TemporaryExtension;

        try
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);

            using (var connection = _database.OpenConnection())
            {
                using var command = connection.CreateCommand();
                command.CommandText = "VACUUM INTO $path;";
                command.Parameters.AddWithValue("$path", temporaryPath);
                command.ExecuteNonQuery();
            }

            File.Move(temporaryPath, finalPath, overwrite: true);
        }
        catch
        {
            TryDelete(temporaryPath);
            throw;
        }

        File.WriteAllText(FingerprintPath(finalPath), currentFingerprint);

        RotateOldBackups();

        return Task.CompletedTask;
    }

    public bool TryRestore()
    {
        foreach (var backup in GetBackupFiles())
        {
            if (!PassesQuickCheck(backup))
                continue;

            var directory = Path.GetDirectoryName(_database.DatabasePath);

            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            TryDelete(_database.DatabasePath + "-wal");
            TryDelete(_database.DatabasePath + "-shm");

            File.Copy(backup, _database.DatabasePath, overwrite: true);

            return true;
        }

        return false;
    }

    private void RotateOldBackups()
    {
        foreach (var backup in GetBackupFiles().Skip(_rotationCount))
        {
            TryDelete(backup);
            TryDelete(FingerprintPath(backup));
        }
    }

    private List<string> GetBackupFiles()
    {
        if (!Directory.Exists(_backupFolder))
            return new List<string>();

        return Directory.GetFiles(_backupFolder, BackupFilePattern)
            .Where(path => path.EndsWith(BackupFileExtension, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(Path.GetFileName)
            .ToList();
    }

    private static string FingerprintPath(string backupPath)
    {
        return Path.ChangeExtension(backupPath, FingerprintExtension);
    }

    private static string? ReadFingerprint(string backupPath)
    {
        var path = FingerprintPath(backupPath);

        return File.Exists(path) ? File.ReadAllText(path) : null;
    }

    private static bool PassesQuickCheck(string backupPath)
    {
        try
        {
            var connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = backupPath,
                Mode = SqliteOpenMode.ReadOnly,
                Pooling = false
            }.ToString();

            using var connection = new SqliteConnection(connectionString);
            connection.Open();

            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA quick_check;";

            return string.Equals(Convert.ToString(command.ExecuteScalar()), "ok", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
        }
    }

    private static string ComputeFingerprint(SqliteConnection connection)
    {
        var killmailCount = ExecuteScalarLong(connection, "SELECT COUNT(*) FROM main.zkill_killmails;");
        var latestCachedAtUtc = ExecuteScalarLong(connection, "SELECT COALESCE(MAX(cached_at_utc), 0) FROM main.zkill_killmails;");
        var attackerCount = ExecuteScalarLong(connection, "SELECT COUNT(*) FROM main.zkill_killmail_attackers;");

        return $"{killmailCount}|{latestCachedAtUtc}|{attackerCount}";
    }

    private static long ExecuteScalarLong(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;

        return Convert.ToInt64(command.ExecuteScalar());
    }
}

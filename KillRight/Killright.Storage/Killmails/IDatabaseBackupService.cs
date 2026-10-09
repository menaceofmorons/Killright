namespace Killright.Storage.Killmails;

public interface IDatabaseBackupService
{
    Task BackupAsync(CancellationToken cancellationToken = default);
    bool TryRestore();
}

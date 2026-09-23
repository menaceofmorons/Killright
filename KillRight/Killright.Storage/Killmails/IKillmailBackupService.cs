namespace Killright.Storage.Killmails;

public interface IKillmailBackupService
{
    Task BackupAsync(CancellationToken cancellationToken = default);
    Task<bool> TryRestoreAsync(CancellationToken cancellationToken = default);
}

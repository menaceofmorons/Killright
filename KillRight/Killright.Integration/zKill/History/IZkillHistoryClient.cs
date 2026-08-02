namespace Killright.Integration.zKill.History;

public interface IZkillHistoryClient
{
    Task<ZkillHistoryPeriodResult> CountWinterNexusQuarterAsync(CancellationToken cancellationToken = default);
}
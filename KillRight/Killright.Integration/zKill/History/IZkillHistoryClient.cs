namespace Killright.Integration.zKill.History;

public interface IZkillHistoryClient
{
    Task<ZkillHistoryMonthResult> CountPreviousCompleteMonthAsync(CancellationToken cancellationToken = default);
}
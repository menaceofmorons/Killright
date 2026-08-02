namespace Killright.Integration.zKill.History;

public interface IZkillHistoryClient
{
    Task<ZkillHistoryPeriodResult> CountCalendarYear2025Async(CancellationToken cancellationToken = default);
}
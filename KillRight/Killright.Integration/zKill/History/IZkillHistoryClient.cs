namespace Killright.Integration.zKill.History;

public interface IZkillHistoryClient
{
    Task<ZkillHistoryDayResult> CountDayAsync(DateOnly date, CancellationToken cancellationToken = default);

    Task<ZkillHistoryPeriodResult> CountCalendarYear2025Async(CancellationToken cancellationToken = default);
}
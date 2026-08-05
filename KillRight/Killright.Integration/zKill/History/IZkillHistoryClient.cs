namespace Killright.Integration.zKill.History;

public interface IZkillHistoryClient
{
    Task<ZkillHistoryDayResult> CountDayAsync(DateOnly date, CancellationToken cancellationToken = default);

    Task<ZkillHistoryEvidenceDayResult> ExtractDayEvidenceAsync(DateOnly date, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ZkillHistoryEvidenceDayResult>> ExtractDaysEvidenceAsync(
        IReadOnlyList<DateOnly> dates,
        CancellationToken cancellationToken = default);

    Task<ZkillHistoryPeriodResult> CountCalendarYear2025Async(CancellationToken cancellationToken = default);
}
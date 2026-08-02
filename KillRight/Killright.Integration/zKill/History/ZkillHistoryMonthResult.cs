namespace Killright.Integration.zKill.History;

public sealed class ZkillHistoryMonthResult
{
    public required DateOnly StartDate { get; init; }
    public required DateOnly EndDate { get; init; }
    public required IReadOnlyList<ZkillHistoryDayResult> Days { get; init; }

    public int TotalRows => Days.Sum(x => x.RowCount);

    public int SuccessfulDays => Days.Count(x => x.Succeeded);

    public int FailedDays => Days.Count(x => !x.Succeeded);

    public string MonthLabel => $"{StartDate:yyyy-MM-dd} to {EndDate:yyyy-MM-dd}";
}
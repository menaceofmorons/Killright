namespace Killright.Integration.zKill.History;

public sealed record ZkillHistoryDayResult(
    DateOnly Date,
    string Url,
    bool Succeeded,
    int RowCount,
    string? ErrorMessage);
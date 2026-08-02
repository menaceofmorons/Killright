namespace Killright.Integration.zKill.History;

public sealed record ZkillHistoryDayResult(
    DateOnly Date,
    string Url,
    bool Succeeded,
    int KillmailCount,
    int AttackerCount,
    int MaxAttackersOnKillmail,
    string? ErrorMessage);
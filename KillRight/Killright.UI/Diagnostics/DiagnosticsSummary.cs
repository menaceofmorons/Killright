namespace Killright.UI.Diagnostics;

public sealed class DiagnosticsSummary
{
    public int IdentityCacheRows { get; init; }
    public int ActivityCacheRows { get; init; }
    public int RecentKillmailRows { get; init; }
    public int DuplicateKillmailRows { get; init; }
    public int ExpiredKillmailRows { get; init; }
    public DateTimeOffset CurrentUtc { get; init; }
    public DateTimeOffset EffectiveUtc { get; init; }
    public int OffsetDays { get; init; }
}
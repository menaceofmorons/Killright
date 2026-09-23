using Killright.Shared.Killmails;

namespace Killright.Shared.zKill;

public sealed record zKillRecentKillmailResult(
    zKillRecentKillmailOutcome Outcome,
    IReadOnlyList<KillmailRecord> Killmails,
    IReadOnlyList<RawKillmail> RawKillmails);

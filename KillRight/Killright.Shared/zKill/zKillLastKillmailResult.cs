using Killright.Shared.Killmails;

namespace Killright.Shared.zKill;

public sealed record zKillLastKillmailResult(
    zKillLastKillmailOutcome Outcome,
    RawKillmail? Killmail,
    zKillActivityType? ActivityType);

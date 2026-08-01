using Killright.Shared;

namespace Killright.Shared.Contracts;

public sealed record KillRightRequest(
    IReadOnlyList<string> PilotNames,
    AnalysisMode AnalysisMode,
    int WindowDays = 7);

using PilotIntel.Shared;

namespace PilotIntel.Shared.Contracts;

public sealed record PilotIntelRequest(
    IReadOnlyList<string> PilotNames,
    AnalysisMode AnalysisMode,
    int WindowDays = 7);

using Killright.Core.Models;
using Killright.Shared;
using Killright.UI.UiState;

namespace Killright.UI.Scan;

public static class ScanPilotSelector
{
    public static IReadOnlyList<Pilot> SelectForAnalysis(
        IReadOnlyList<Pilot> resolvedPilots,
        IReadOnlyList<IgnoreListEntry> ignoreList)
    {
        return resolvedPilots
            .Where(pilot => pilot.VerifyStatus is not (VerifyStatus.NoMatch or VerifyStatus.Failed))
            .Where(pilot => !IgnoreListFilter.IsIgnored(pilot, ignoreList))
            .ToList();
    }
}

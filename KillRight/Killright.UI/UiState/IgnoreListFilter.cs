using Killright.Core.Models;
using Killright.Shared;

namespace Killright.UI.UiState;

public static class IgnoreListFilter
{
    public static bool IsIgnored(Pilot pilot, IReadOnlyList<IgnoreListEntry> ignoreList)
    {
        foreach (var entry in ignoreList)
        {
            var matches = entry.Type switch
            {
                IgnoreEntryType.Pilot => pilot.CharacterId == entry.Id,
                IgnoreEntryType.Corporation => pilot.Corporation?.CorporationId == entry.Id,
                IgnoreEntryType.Alliance => pilot.AllianceId == entry.Id,
                _ => false
            };

            if (matches)
                return true;
        }

        return false;
    }
}

using Killright.UI.Analysis;
using Killright.UI.Configuration;

namespace Killright.UI.ViewModels;

public static class RelationshipConfidenceBandMapper
{
    public static string MapScore(int confidence, IReadOnlyList<RelationshipConfidenceBandSetting> bands)
    {
        for (var index = 0; index < bands.Count; index++)
        {
            if (confidence >= bands[index].MinimumScore && confidence <= bands[index].MaximumScore)
                return ThreatBandMapper.PositionKey(index + 1);
        }

        return string.Empty;
    }
}

using Killright.UI.Configuration;

namespace Killright.UI.ViewModels;

public static class RelationshipConfidenceBandMapper
{
    public static string MapScore(int confidence, IReadOnlyList<RelationshipConfidenceBandSetting> bands)
    {
        foreach (var band in bands)
        {
            if (confidence >= band.MinimumScore && confidence <= band.MaximumScore)
                return band.Name;
        }

        return string.Empty;
    }
}

using System.Windows.Media;
using Killright.UI.Configuration;

namespace Killright.UI.ViewModels;

public static class RelationshipHighlightCalculator
{
    public static void Apply(
        IReadOnlyList<PilotReportRow> rows,
        PilotReportRow? hovered,
        Func<long, bool> isNpcCorporation,
        IReadOnlyList<RelationshipConfidenceBandSetting> confidenceBands,
        Color pilotColor,
        Color relatedColor,
        double highlightOpacity)
    {
        if (hovered?.CharacterId is not long hoveredCharacterId)
        {
            Clear(rows);
            return;
        }

        var npcCache = new Dictionary<long, bool>();

        bool IsNpc(long corporationId) => npcCache.TryGetValue(corporationId, out var cached)
            ? cached
            : npcCache[corporationId] = isNpcCorporation(corporationId);

        foreach (var row in rows)
        {
            if (ReferenceEquals(row, hovered))
            {
                row.HighlightBrush = new SolidColorBrush(HighlightColorCalculator.ForPilot(pilotColor, highlightOpacity));
                row.RelationshipStrengthDisplay = "-";
                row.RelationshipConfidenceBand = null;
                continue;
            }

            if (IsSameGroup(row, hovered, IsNpc))
            {
                row.HighlightBrush = new SolidColorBrush(HighlightColorCalculator.ForSameGroup(pilotColor, highlightOpacity));
                row.RelationshipStrengthDisplay = "-";
                row.RelationshipConfidenceBand = null;
                continue;
            }

            var relationship = row.CharacterId is long characterId
                ? row.GroupRelationships.FirstOrDefault(candidate =>
                    (candidate.PilotACharacterId == hoveredCharacterId && candidate.PilotBCharacterId == characterId) ||
                    (candidate.PilotBCharacterId == hoveredCharacterId && candidate.PilotACharacterId == characterId))
                : null;

            if (relationship is not null)
            {
                row.HighlightBrush = new SolidColorBrush(HighlightColorCalculator.ForRelated(relatedColor, highlightOpacity));
                row.RelationshipStrengthDisplay = relationship.Strength.ToString();
                row.RelationshipConfidenceBand = RelationshipConfidenceBandMapper.MapScore(relationship.Confidence, confidenceBands);
                continue;
            }

            row.HighlightBrush = null;
            row.RelationshipStrengthDisplay = "-";
            row.RelationshipConfidenceBand = null;
        }
    }

    public static void Clear(IReadOnlyList<PilotReportRow> rows)
    {
        foreach (var row in rows)
        {
            row.HighlightBrush = null;
            row.RelationshipStrengthDisplay = "-";
            row.RelationshipConfidenceBand = null;
        }
    }

    private static bool IsSameGroup(PilotReportRow row, PilotReportRow hovered, Func<long, bool> isNpcCorporation)
    {
        if (row.CorporationId is long corporationId && hovered.CorporationId == corporationId && !isNpcCorporation(corporationId))
            return true;

        return row.AllianceId is long allianceId && hovered.AllianceId == allianceId;
    }
}

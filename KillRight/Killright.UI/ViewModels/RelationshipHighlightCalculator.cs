using System.Windows.Media;
using Killright.UI.Analysis;
using Killright.UI.Configuration;

namespace Killright.UI.ViewModels;

public static class RelationshipHighlightCalculator
{
    private enum RowRole
    {
        Unrelated,
        Pilot,
        SameGroup,
        Related
    }

    public static void Apply(
        IReadOnlyList<PilotReportRow> rows,
        PilotReportRow? hovered,
        Func<long, bool> isNpcCorporation,
        IReadOnlyList<RelationshipConfidenceBandSetting> confidenceBands,
        Color pilotColor,
        Color relatedColor,
        double highlightOpacity)
    {
        if (hovered?.CharacterId is null)
        {
            Clear(rows);
            return;
        }

        var isNpc = Memoize(isNpcCorporation);

        ApplyRelationshipValues(rows, hovered, isNpc, confidenceBands);
        ApplyTints(rows, hovered, isNpc, pilotColor, relatedColor, highlightOpacity);
    }

    public static void ApplyRelationshipValues(
        IReadOnlyList<PilotReportRow> rows,
        PilotReportRow pilot,
        Func<long, bool> isNpcCorporation,
        IReadOnlyList<RelationshipConfidenceBandSetting> confidenceBands)
    {
        if (pilot.CharacterId is null)
        {
            foreach (var row in rows)
                ResetRelationshipValues(row);

            return;
        }

        var isNpc = Memoize(isNpcCorporation);

        foreach (var row in rows)
        {
            var role = Classify(row, pilot, isNpc, out var relationship);

            if (role == RowRole.Related && relationship is not null)
            {
                row.RelationshipStrengthDisplay = relationship.Strength.ToString();
                row.RelationshipConfidenceBand = RelationshipConfidenceBandMapper.MapScore(relationship.Confidence, confidenceBands);
            }
            else
            {
                ResetRelationshipValues(row);
            }
        }
    }

    public static void ApplyTints(
        IReadOnlyList<PilotReportRow> rows,
        PilotReportRow pilot,
        Func<long, bool> isNpcCorporation,
        Color pilotColor,
        Color relatedColor,
        double highlightOpacity)
    {
        if (pilot.CharacterId is null)
        {
            foreach (var row in rows)
                row.HighlightBrush = null;

            return;
        }

        var isNpc = Memoize(isNpcCorporation);

        foreach (var row in rows)
        {
            row.HighlightBrush = Classify(row, pilot, isNpc, out _) switch
            {
                RowRole.Pilot => new SolidColorBrush(HighlightColorCalculator.ForPilot(pilotColor, highlightOpacity)),
                RowRole.SameGroup => new SolidColorBrush(HighlightColorCalculator.ForSameGroup(pilotColor, highlightOpacity)),
                RowRole.Related => new SolidColorBrush(HighlightColorCalculator.ForRelated(relatedColor, highlightOpacity)),
                _ => null
            };
        }
    }

    public static void Clear(IReadOnlyList<PilotReportRow> rows)
    {
        foreach (var row in rows)
        {
            row.HighlightBrush = null;
            ResetRelationshipValues(row);
        }
    }

    public static bool IsSameGroup(PilotReportRow row, PilotReportRow other, Func<long, bool> isNpcCorporation)
    {
        if (row.CorporationId is long corporationId && other.CorporationId == corporationId && !isNpcCorporation(corporationId))
            return true;

        return row.AllianceId is long allianceId && other.AllianceId == allianceId;
    }

    public static PilotRelationship? FindRelationship(PilotReportRow row, PilotReportRow other)
    {
        if (row.CharacterId is not long characterId || other.CharacterId is not long otherCharacterId)
            return null;

        return row.GroupRelationships.FirstOrDefault(candidate =>
            (candidate.PilotACharacterId == otherCharacterId && candidate.PilotBCharacterId == characterId) ||
            (candidate.PilotBCharacterId == otherCharacterId && candidate.PilotACharacterId == characterId));
    }

    private static RowRole Classify(PilotReportRow row, PilotReportRow pilot, Func<long, bool> isNpc, out PilotRelationship? relationship)
    {
        relationship = null;

        if (ReferenceEquals(row, pilot))
            return RowRole.Pilot;

        if (IsSameGroup(row, pilot, isNpc))
            return RowRole.SameGroup;

        relationship = FindRelationship(row, pilot);
        return relationship is not null ? RowRole.Related : RowRole.Unrelated;
    }

    private static void ResetRelationshipValues(PilotReportRow row)
    {
        row.RelationshipStrengthDisplay = "-";
        row.RelationshipConfidenceBand = null;
    }

    private static Func<long, bool> Memoize(Func<long, bool> isNpcCorporation)
    {
        var cache = new Dictionary<long, bool>();

        return corporationId => cache.TryGetValue(corporationId, out var cached)
            ? cached
            : cache[corporationId] = isNpcCorporation(corporationId);
    }
}

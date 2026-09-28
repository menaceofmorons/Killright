using Killright.UI.Analysis;

namespace Killright.UI.ViewModels;

public static class PilotGroupCountAnnotator
{
    public static void Annotate(IReadOnlyList<PilotReportRow> rows, IReadOnlySet<long> npcCorporationIds)
    {
        AnnotateAlliance(rows);
        AnnotateCorporation(rows, npcCorporationIds);
        AnnotateGroupCell(rows);
    }

    private static void AnnotateGroupCell(IReadOnlyList<PilotReportRow> rows)
    {
        foreach (var row in rows)
        {
            var directCount = row.GroupRelationships.Count(relationship => relationship.LinkType == RelationshipLinkType.Direct);
            var chainCount = row.GroupRelationships.Count(relationship => relationship.LinkType == RelationshipLinkType.Chain);

            row.Group = $"{FormatGroupCount(directCount)}/{FormatGroupCount(chainCount)}";
        }
    }

    private static string FormatGroupCount(int count) => count == 0 ? "-" : count.ToString();

    private static void AnnotateAlliance(IReadOnlyList<PilotReportRow> rows)
    {
        var counts = rows
            .Where(row => row.AllianceId is not null)
            .GroupBy(row => row.AllianceId!.Value)
            .ToDictionary(group => group.Key, group => group.Count());

        foreach (var row in rows)
        {
            if (row.AllianceId is null || !counts.TryGetValue(row.AllianceId.Value, out var count) || count < 2)
                continue;

            row.Alliance = $"{row.Alliance} [{count}]";
        }
    }

    private static void AnnotateCorporation(IReadOnlyList<PilotReportRow> rows, IReadOnlySet<long> npcCorporationIds)
    {
        var counts = rows
            .Where(row => row.CorporationId is not null && !npcCorporationIds.Contains(row.CorporationId.Value))
            .GroupBy(row => row.CorporationId!.Value)
            .ToDictionary(group => group.Key, group => group.Count());

        foreach (var row in rows)
        {
            if (row.CorporationId is null
                || npcCorporationIds.Contains(row.CorporationId.Value)
                || !counts.TryGetValue(row.CorporationId.Value, out var count)
                || count < 2)
                continue;

            row.Corporation = $"{row.Corporation} [{count}]";
        }
    }
}

namespace Killright.UI.ViewModels;

public static class PilotGroupCountAnnotator
{
    public static void Annotate(IReadOnlyList<PilotReportRow> rows, long npcCorporationIdThreshold)
    {
        AnnotateAlliance(rows);
        AnnotateCorporation(rows, npcCorporationIdThreshold);
    }

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

    private static void AnnotateCorporation(IReadOnlyList<PilotReportRow> rows, long npcCorporationIdThreshold)
    {
        var counts = rows
            .Where(row => row.CorporationId is not null && row.CorporationId.Value >= npcCorporationIdThreshold)
            .GroupBy(row => row.CorporationId!.Value)
            .ToDictionary(group => group.Key, group => group.Count());

        foreach (var row in rows)
        {
            if (row.CorporationId is null
                || row.CorporationId.Value < npcCorporationIdThreshold
                || !counts.TryGetValue(row.CorporationId.Value, out var count)
                || count < 2)
                continue;

            row.Corporation = $"{row.Corporation} [{count}]";
        }
    }
}

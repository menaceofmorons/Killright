namespace Killright.UI.ViewModels;

public static class NewPilotFlagger
{
    public static IReadOnlySet<string> Apply(IReadOnlyList<PilotReportRow> rows, IReadOnlySet<string>? previousKeys)
    {
        var currentKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var row in rows)
            currentKeys.Add(KeyOf(row));

        var sharesPilot = previousKeys is not null && currentKeys.Overlaps(previousKeys);

        foreach (var row in rows)
            row.IsNewPilot = sharesPilot && !previousKeys!.Contains(KeyOf(row));

        return currentKeys;
    }

    private static string KeyOf(PilotReportRow row) =>
        row.CharacterId is long characterId ? $"id:{characterId}" : $"name:{row.InputName}";
}

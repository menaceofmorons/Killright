using System.Linq;

namespace Killright.UI.UiState;

public enum AppTheme
{
    Light,
    Dark,
    FollowWindows
}

public enum GridFontTier
{
    Large,
    Medium,
    Small,
    Tiny
}

public static class ColumnIds
{
    public const string Pilot = "Pilot";
    public const string Verify = "Verify";
    public const string Threat = "Threat";
    public const string SecurityStatus = "SecurityStatus";
    public const string Group = "Group";
    public const string Corporation = "Corporation";
    public const string Alliance = "Alliance";
    public const string Style = "Style";
    public const string Week = "Week";
    public const string LastActive = "LastActive";
    public const string Notes = "Notes";

    internal const string GeneralStyle = "GeneralStyle";
    internal const string RecentStyle = "RecentStyle";
    internal const string KillsWeek = "KillsWeek";
    internal const string SoloWeek = "SoloWeek";
}

public sealed record ColumnState
{
    public string Id { get; init; } = string.Empty;

    public int DisplayIndex { get; init; }

    public double Width { get; init; }

    public bool Visible { get; init; } = true;
}

public sealed record UiStateModel
{
    public int Version { get; init; } = UiStateDefaults.CurrentVersion;

    public double WindowLeft { get; init; }

    public double WindowTop { get; init; }

    public double WindowWidth { get; init; } = UiStateDefaults.WindowWidth;

    public double WindowHeight { get; init; } = UiStateDefaults.WindowHeight;

    public bool AlwaysOnTop { get; init; } = UiStateDefaults.AlwaysOnTop;

    public AppTheme Theme { get; init; } = UiStateDefaults.Theme;

    public GridFontTier GridFontTier { get; init; } = UiStateDefaults.DefaultGridFontTier;

    public IReadOnlyList<ColumnState> Columns { get; init; } = UiStateDefaults.DefaultColumns;

    public bool DeveloperTabRevealed { get; init; } = UiStateDefaults.DeveloperTabRevealed;
}

public static class UiStateDefaults
{
    public const int CurrentVersion = 2;
    public const double WindowWidth = 720;
    public const double WindowHeight = 360;
    public const bool AlwaysOnTop = true;
    public const AppTheme Theme = AppTheme.FollowWindows;
    public const GridFontTier DefaultGridFontTier = GridFontTier.Medium;
    public const bool DeveloperTabRevealed = false;

    public static readonly IReadOnlyList<(string Id, double Width, string Label, bool RequiresDeveloperMode, bool DefaultVisible)> ColumnCatalog = new[]
    {
        (ColumnIds.Threat, 50d, "Threat", false, true),
        (ColumnIds.Pilot, 170d, "Pilot", false, true),
        (ColumnIds.Style, 60d, "Style", false, true),
        (ColumnIds.SecurityStatus, 70d, "Sec", false, true),
        (ColumnIds.Week, 60d, "Week", false, true),
        (ColumnIds.Corporation, 210d, "Corporation", false, true),
        (ColumnIds.Alliance, 210d, "Alliance", false, true),
        (ColumnIds.LastActive, 90d, "Last Kill", false, true),
        (ColumnIds.Notes, 260d, "Notes", true, false),
        (ColumnIds.Verify, 70d, "Verify", true, false),
        (ColumnIds.Group, 90d, "Group", false, false)
    };

    private static readonly IReadOnlyList<(string FirstRetiredId, string SecondRetiredId, string MergedId)> RetiredColumnMigrations = new[]
    {
        (ColumnIds.GeneralStyle, ColumnIds.RecentStyle, ColumnIds.Style),
        (ColumnIds.KillsWeek, ColumnIds.SoloWeek, ColumnIds.Week)
    };

    public static IReadOnlyList<ColumnState> DefaultColumns { get; } = ColumnCatalog
        .Select((catalogEntry, index) => new ColumnState
        {
            Id = catalogEntry.Id,
            DisplayIndex = index,
            Width = catalogEntry.Width,
            Visible = catalogEntry.DefaultVisible
        })
        .ToList();

    public static bool IsEffectivelyVisible(ColumnState column, bool developerTabRevealed)
    {
        var catalogEntry = ColumnCatalog.FirstOrDefault(entry => entry.Id == column.Id);

        if (catalogEntry.RequiresDeveloperMode && !developerTabRevealed)
            return false;

        return column.Visible;
    }

    public static IReadOnlyList<ColumnState> ReconcileColumns(IReadOnlyList<ColumnState> saved)
    {
        var migrated = MigrateRetiredColumns(saved);
        var knownIds = ColumnCatalog.Select(catalogEntry => catalogEntry.Id).ToHashSet();
        var ordered = migrated.Where(column => knownIds.Contains(column.Id)).OrderBy(column => column.DisplayIndex).ToList();
        var presentIds = ordered.Select(column => column.Id).ToHashSet();

        foreach (var catalogEntry in ColumnCatalog)
        {
            if (presentIds.Contains(catalogEntry.Id))
                continue;

            ordered.Add(new ColumnState { Id = catalogEntry.Id, Width = catalogEntry.Width, Visible = catalogEntry.DefaultVisible });
        }

        return ordered
            .Select((column, index) => column with
            {
                DisplayIndex = index,
                Visible = column.Id == ColumnIds.Pilot || column.Visible
            })
            .ToList();
    }

    private static IReadOnlyList<ColumnState> MigrateRetiredColumns(IReadOnlyList<ColumnState> saved)
    {
        var working = saved.ToList();

        foreach (var (firstRetiredId, secondRetiredId, mergedId) in RetiredColumnMigrations)
        {
            var retired = working.Where(column => column.Id == firstRetiredId || column.Id == secondRetiredId).ToList();

            if (retired.Count == 0 || working.Any(column => column.Id == mergedId))
                continue;

            var mergedCatalogEntry = ColumnCatalog.First(entry => entry.Id == mergedId);

            working.RemoveAll(column => column.Id == firstRetiredId || column.Id == secondRetiredId);
            working.Add(new ColumnState
            {
                Id = mergedId,
                DisplayIndex = retired.Min(column => column.DisplayIndex),
                Width = mergedCatalogEntry.Width,
                Visible = retired.Any(column => column.Visible)
            });
        }

        return working;
    }
}

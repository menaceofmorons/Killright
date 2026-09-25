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
    public const string GeneralStyle = "GeneralStyle";
    public const string RecentStyle = "RecentStyle";
    public const string KillsWeek = "KillsWeek";
    public const string SoloWeek = "SoloWeek";
    public const string LastActive = "LastActive";
    public const string Notes = "Notes";
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
    public const int CurrentVersion = 1;
    public const double WindowWidth = 720;
    public const double WindowHeight = 360;
    public const bool AlwaysOnTop = true;
    public const AppTheme Theme = AppTheme.FollowWindows;
    public const GridFontTier DefaultGridFontTier = GridFontTier.Medium;
    public const bool DeveloperTabRevealed = false;

    public static readonly IReadOnlyList<(string Id, double Width, string Label)> ColumnCatalog = new[]
    {
        (ColumnIds.Pilot, 170d, "Pilot"),
        (ColumnIds.Verify, 70d, "Verify"),
        (ColumnIds.Threat, 90d, "Threat"),
        (ColumnIds.SecurityStatus, 70d, "Sec"),
        (ColumnIds.Group, 90d, "Group"),
        (ColumnIds.Corporation, 210d, "Corporation"),
        (ColumnIds.Alliance, 210d, "Alliance"),
        (ColumnIds.GeneralStyle, 120d, "General Style"),
        (ColumnIds.RecentStyle, 120d, "Recent Style"),
        (ColumnIds.KillsWeek, 95d, "Kills Week"),
        (ColumnIds.SoloWeek, 95d, "Solo Week"),
        (ColumnIds.LastActive, 160d, "Last Active"),
        (ColumnIds.Notes, 260d, "Notes")
    };

    public static IReadOnlyList<ColumnState> DefaultColumns { get; } = ColumnCatalog
        .Select((catalogEntry, index) => new ColumnState
        {
            Id = catalogEntry.Id,
            DisplayIndex = index,
            Width = catalogEntry.Width,
            Visible = true
        })
        .ToList();

    public static IReadOnlyList<ColumnState> ReconcileColumns(IReadOnlyList<ColumnState> saved)
    {
        var knownIds = ColumnCatalog.Select(catalogEntry => catalogEntry.Id).ToHashSet();
        var ordered = saved.Where(column => knownIds.Contains(column.Id)).OrderBy(column => column.DisplayIndex).ToList();
        var presentIds = ordered.Select(column => column.Id).ToHashSet();

        foreach (var catalogEntry in ColumnCatalog)
        {
            if (presentIds.Contains(catalogEntry.Id))
                continue;

            ordered.Add(new ColumnState { Id = catalogEntry.Id, Width = catalogEntry.Width, Visible = true });
        }

        return ordered
            .Select((column, index) => column with
            {
                DisplayIndex = index,
                Visible = column.Id == ColumnIds.Pilot || column.Visible
            })
            .ToList();
    }
}

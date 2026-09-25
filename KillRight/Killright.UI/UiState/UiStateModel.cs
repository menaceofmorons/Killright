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
}

public static class UiStateDefaults
{
    public const int CurrentVersion = 1;
    public const double WindowWidth = 720;
    public const double WindowHeight = 360;
    public const bool AlwaysOnTop = true;
    public const AppTheme Theme = AppTheme.FollowWindows;
    public const GridFontTier DefaultGridFontTier = GridFontTier.Medium;
}

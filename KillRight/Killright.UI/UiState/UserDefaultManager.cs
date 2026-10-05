namespace Killright.UI.UiState;

public static class UserDefaultManager
{
    public static UserDefaultSnapshot CaptureFrom(UiStateModel state)
    {
        return new UserDefaultSnapshot
        {
            WindowLeft = state.WindowLeft,
            WindowTop = state.WindowTop,
            WindowWidth = state.WindowWidth,
            WindowHeight = state.WindowHeight,
            AlwaysOnTop = state.AlwaysOnTop,
            Theme = state.Theme,
            GridFontTier = state.GridFontTier,
            Columns = state.Columns,
            PilotHighlightColorHex = state.PilotHighlightColorHex,
            RelatedHighlightColorHex = state.RelatedHighlightColorHex,
            NewPilotColorHex = state.NewPilotColorHex
        };
    }

    public static UiStateModel ApplyTo(
        UiStateModel state,
        UserDefaultSnapshot snapshot,
        double virtualScreenLeft,
        double virtualScreenTop,
        double virtualScreenWidth,
        double virtualScreenHeight)
    {
        var (left, top) = WindowBoundsCalculator.ClampToVirtualScreen(
            snapshot.WindowLeft,
            snapshot.WindowTop,
            snapshot.WindowWidth,
            snapshot.WindowHeight,
            virtualScreenLeft,
            virtualScreenTop,
            virtualScreenWidth,
            virtualScreenHeight);

        return state with
        {
            WindowLeft = left,
            WindowTop = top,
            WindowWidth = snapshot.WindowWidth,
            WindowHeight = snapshot.WindowHeight,
            AlwaysOnTop = snapshot.AlwaysOnTop,
            Theme = snapshot.Theme,
            GridFontTier = snapshot.GridFontTier,
            Columns = UiStateDefaults.ReconcileColumns(snapshot.Columns),
            PilotHighlightColorHex = snapshot.PilotHighlightColorHex,
            RelatedHighlightColorHex = snapshot.RelatedHighlightColorHex,
            NewPilotColorHex = snapshot.NewPilotColorHex
        };
    }
}

namespace Killright.UI.ViewModels;

public sealed class PilotHoldState
{
    public PilotReportRow? Held { get; private set; }

    public PilotReportRow? Hovered { get; private set; }

    public PilotReportRow? Filtered { get; private set; }

    public PilotReportRow? RelationshipPilot => Filtered ?? Held ?? Hovered;

    public PilotReportRow? HighlightPilot => Filtered is null ? Held ?? Hovered : null;

    public bool ToggleHold(PilotReportRow row)
    {
        if (ReferenceEquals(Held, row))
        {
            Held = null;
            return false;
        }

        if (row.CharacterId is null)
            return false;

        Held = row;
        return true;
    }

    public void Hover(PilotReportRow? row) => Hovered = row;

    public void SetFiltered(PilotReportRow? row) => Filtered = row;

    public void ReleaseHold() => Held = null;

    public void Reset()
    {
        Held = null;
        Hovered = null;
        Filtered = null;
    }
}

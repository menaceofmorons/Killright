namespace Killright.UI.ViewModels;

public sealed class PilotLastActivitySummary
{
    public required string DateTime { get; init; }
    public required string KillLoss { get; init; }
    public required string System { get; init; }
    public required string Ship { get; init; }
    public required string Weapon { get; init; }
    public required string Victim { get; init; }
    public required string Attackers { get; init; }
    public required bool IsKill { get; init; }
}

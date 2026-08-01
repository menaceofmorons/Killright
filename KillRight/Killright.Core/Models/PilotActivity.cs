namespace Killright.Core.Models;

public sealed record PilotActivity
{
    public required long CharacterId { get; init; }
    public int KillsWeek { get; init; }
    public int SoloWeek { get; init; }
    public int SmallGangWeek { get; init; }
    public int BlobWeek { get; init; }
    public int FleetWeek { get; init; }
    public int LifetimeKills { get; init; }
    public int LifetimeSoloKills { get; init; }
}

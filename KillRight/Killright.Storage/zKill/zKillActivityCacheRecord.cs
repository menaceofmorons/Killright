using Killright.Integration.zKill;

namespace Killright.Storage.zKill;

public sealed class zKillActivityCacheRecord
{
    public required long CharacterId { get; init; }
    public required bool HasPublicActivityData { get; init; }
    public int? KillsWeek { get; init; }
    public int? SoloWeek { get; init; }
    public DateTimeOffset? LastActiveUtc { get; init; }
    public zKillActivityType? LastActivityType { get; init; }
    public required DateTimeOffset CheckedAtUtc { get; init; }
    public string? Error { get; init; }

    public zKillActivity ToActivity()
    {
        return new zKillActivity(
            CharacterId,
            HasPublicActivityData,
            KillsWeek,
            SoloWeek,
            LastActiveUtc,
            LastActivityType,
            CheckedAtUtc,
            Error);
    }

    public static zKillActivityCacheRecord FromActivity(zKillActivity activity)
    {
        return new zKillActivityCacheRecord
        {
            CharacterId = activity.CharacterId,
            HasPublicActivityData = activity.HasPublicActivityData,
            KillsWeek = activity.KillsWeek,
            SoloWeek = activity.SoloWeek,
            LastActiveUtc = activity.LastActiveUtc,
            LastActivityType = activity.LastActivityType,
            CheckedAtUtc = activity.CheckedAtUtc,
            Error = activity.Error
        };
    }
}
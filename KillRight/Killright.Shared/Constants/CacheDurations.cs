namespace Killright.Shared.Constants;

public static class CacheDurations
{
    public static readonly TimeSpan PilotIdentity = TimeSpan.FromHours(24);
    public static readonly TimeSpan zKillActivity = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan zKillStatistics = TimeSpan.FromDays(30);
}
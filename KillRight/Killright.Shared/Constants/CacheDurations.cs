namespace Killright.Shared.Constants;

public static class CacheDurations
{
    public static readonly TimeSpan PilotIdentity = TimeSpan.FromHours(24);
    public static readonly TimeSpan SecurityStatus = TimeSpan.FromHours(1);
    public static readonly TimeSpan zKillStatistics = TimeSpan.FromDays(30);
    public static readonly TimeSpan LastKillmailLookup = TimeSpan.FromDays(7) - TimeSpan.FromHours(1);
}
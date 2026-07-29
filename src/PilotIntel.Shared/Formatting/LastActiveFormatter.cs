using PilotIntel.Shared.Time;

namespace PilotIntel.Shared.Formatting;

public static class LastActiveFormatter
{
    public static string Format(DateTimeOffset? lastActiveUtc, string? activityType)
    {
        if (lastActiveUtc is null)
            return "-";

        var age = ApplicationClock.UtcNow - lastActiveUtc.Value;
        var suffix = GetSuffix(activityType);

        if (age.TotalHours < 24)
            return $"{lastActiveUtc.Value:HHmm}{suffix}";

        if (age.TotalDays < 30)
            return $"{(int)age.TotalDays}d{suffix}";

        return $">30d{suffix}";
    }

    private static string GetSuffix(string? activityType)
    {
        return activityType switch
        {
            "Kill" => "(k)",
            "Loss" => "(l)",
            _ => string.Empty
        };
    }
}
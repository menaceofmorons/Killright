namespace Killright.Shared.Time;

public static class ApplicationClock
{
    public static int OffsetDays { get; private set; }

    public static DateTimeOffset UtcNow => DateTimeOffset.UtcNow.AddDays(OffsetDays);

    public static void SetOffsetDays(int days)
    {
        OffsetDays = days;
    }

    public static void AddDays(int days)
    {
        OffsetDays += days;
    }

    public static void Reset()
    {
        OffsetDays = 0;
    }
}
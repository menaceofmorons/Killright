namespace Killright.Shared.Data;

public static class SqlValueFormatter
{
    public static object Bool(bool value)
    {
        return value ? 1L : 0L;
    }

    public static object Int(int? value)
    {
        return value is null ? DBNull.Value : (long)value.Value;
    }

    public static object Long(long? value)
    {
        return value is null ? DBNull.Value : value.Value;
    }

    public static object Double(double? value)
    {
        if (value is null)
            return DBNull.Value;

        if (!double.IsFinite(value.Value))
            throw new ArgumentOutOfRangeException(nameof(value), value, "A non-finite double cannot be stored.");

        return value.Value;
    }

    public static object String(string? value)
    {
        return value is null ? DBNull.Value : value;
    }

    public static object Date(DateTimeOffset? value)
    {
        return value is null ? DBNull.Value : value.Value.ToUnixTimeSeconds();
    }

    public static object Date(DateTime? value)
    {
        return value is null ? DBNull.Value : new DateTimeOffset(value.Value.ToUniversalTime()).ToUnixTimeSeconds();
    }

    public static object Date(DateOnly? value)
    {
        return value is null
            ? DBNull.Value
            : new DateTimeOffset(value.Value.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero).ToUnixTimeSeconds();
    }
}

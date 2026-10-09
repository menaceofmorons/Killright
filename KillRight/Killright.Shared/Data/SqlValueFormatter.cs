using System.Globalization;

namespace Killright.Shared.Data;

public static class SqlValueFormatter
{
    public static string Bool(bool value)
    {
        return value ? "1" : "0";
    }

    public static string Int(int? value)
    {
        return value?.ToString(CultureInfo.InvariantCulture) ?? "NULL";
    }

    public static string Long(long? value)
    {
        return value?.ToString(CultureInfo.InvariantCulture) ?? "NULL";
    }

    public static string Double(double? value)
    {
        return value?.ToString(CultureInfo.InvariantCulture) ?? "NULL";
    }

    public static string String(string? value)
    {
        return value is null ? "NULL" : $"'{Escape(value)}'";
    }

    public static string Date(DateTimeOffset? value)
    {
        return value is null ? "NULL" : value.Value.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
    }

    public static string Date(DateTime value)
    {
        return new DateTimeOffset(value.ToUniversalTime()).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
    }

    public static string Date(DateOnly? value)
    {
        return value is null
            ? "NULL"
            : new DateTimeOffset(value.Value.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
    }

    public static string Escape(string value)
    {
        return value.Replace("'", "''");
    }
}
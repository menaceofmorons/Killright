using System.Globalization;

namespace PilotIntel.Shared.Data;

public static class SqlValueFormatter
{
    public static string Bool(bool value)
    {
        return value ? "TRUE" : "FALSE";
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
        return value is null ? "NULL" : $"'{value.Value.UtcDateTime:O}'";
    }

    public static string Date(DateTime value)
    {
        return $"'{value.ToUniversalTime():O}'";
    }

    public static string Escape(string value)
    {
        return value.Replace("'", "''");
    }
}
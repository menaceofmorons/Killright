using System.Data;

namespace Killright.Shared.Data;

public static class DataRecordExtensions
{
    public static string? GetNullableString(this IDataRecord reader, int ordinal)
    {
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    public static int? GetNullableInt32(this IDataRecord reader, int ordinal)
    {
        return reader.IsDBNull(ordinal) ? null : reader.GetInt32(ordinal);
    }

    public static long? GetNullableInt64(this IDataRecord reader, int ordinal)
    {
        return reader.IsDBNull(ordinal) ? null : reader.GetInt64(ordinal);
    }

    public static double? GetNullableDouble(this IDataRecord reader, int ordinal)
    {
        return reader.IsDBNull(ordinal) ? null : reader.GetDouble(ordinal);
    }

    public static bool? GetNullableBoolean(this IDataRecord reader, int ordinal)
    {
        return reader.IsDBNull(ordinal) ? null : reader.GetBoolean(ordinal);
    }

    public static DateTimeOffset GetUtcDateTimeOffset(this IDataRecord reader, int ordinal)
    {
        return DateTimeOffset.FromUnixTimeSeconds(reader.GetInt64(ordinal));
    }

    public static DateTimeOffset? GetNullableDateTimeOffset(this IDataRecord reader, int ordinal)
    {
        return reader.IsDBNull(ordinal) ? null : DateTimeOffset.FromUnixTimeSeconds(reader.GetInt64(ordinal));
    }

    public static DateOnly GetDateOnly(this IDataRecord reader, int ordinal)
    {
        return DateOnly.FromDateTime(DateTimeOffset.FromUnixTimeSeconds(reader.GetInt64(ordinal)).UtcDateTime);
    }

    public static DateOnly? GetNullableDateOnly(this IDataRecord reader, int ordinal)
    {
        return reader.IsDBNull(ordinal) ? null : reader.GetDateOnly(ordinal);
    }
}

using System.Data;
using System.Globalization;

namespace PilotIntel.Shared.Data;

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

    public static DateTimeOffset GetDateTimeOffset(this IDataRecord reader, int ordinal)
    {
        var value = reader.GetString(ordinal);
        return DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);
    }

    public static DateTimeOffset? GetNullableDateTimeOffset(this IDataRecord reader, int ordinal)
    {
        if (reader.IsDBNull(ordinal))
            return null;

        var value = reader.GetString(ordinal);
        return DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);
    }
}
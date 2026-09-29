using System.Globalization;
using System.Text;

namespace Killright.Storage.Diagnostics;

public static class ScanTimingLog
{
    public const string Header = "utc_timestamp,scan_id,pilot_count,level,character_id,phase,milliseconds,tag";

    public static void Append(string path, IReadOnlyList<TimingRow> rows)
    {
        try
        {
            if (rows.Count == 0)
                return;

            var builder = new StringBuilder();

            if (!File.Exists(path))
                builder.Append(Header).Append(Environment.NewLine);

            foreach (var row in rows)
                builder.Append(FormatLine(row)).Append(Environment.NewLine);

            File.AppendAllText(path, builder.ToString());
        }
        catch
        {
        }
    }

    public static string FormatLine(TimingRow row)
    {
        return string.Join(
            ",",
            row.UtcTimestamp.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture),
            Escape(row.ScanId),
            row.PilotCount.ToString(CultureInfo.InvariantCulture),
            Escape(row.Level),
            row.CharacterId?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            Escape(row.Phase),
            row.Milliseconds.ToString("0.###", CultureInfo.InvariantCulture),
            Escape(row.Tag ?? string.Empty));
    }

    private static string Escape(string value)
    {
        return value.IndexOfAny(new[] { ',', '"', '\r', '\n' }) < 0
            ? value
            : "\"" + value.Replace("\"", "\"\"") + "\"";
    }
}

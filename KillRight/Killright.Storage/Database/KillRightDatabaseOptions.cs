using System.Globalization;
using System.Text.RegularExpressions;

namespace Killright.Storage.Database;

public sealed class KillRightDatabaseOptions
{
    public const string DefaultMemoryLimit = "1GB";

    public const int DefaultThreads = 4;

    public const int DefaultIdleCheckpointSeconds = 30;

    private static readonly Regex MemoryLimitPattern = new(
        @"^(?<amount>\d+(\.\d+)?)\s?(B|KB|MB|GB|TB|KiB|MiB|GiB|TiB)$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public string DatabasePath { get; init; } = string.Empty;

    public string MemoryLimit { get; init; } = DefaultMemoryLimit;

    public int Threads { get; init; } = DefaultThreads;

    public static bool IsValidMemoryLimit(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var match = MemoryLimitPattern.Match(value.Trim());

        return match.Success
            && decimal.TryParse(match.Groups["amount"].Value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var amount)
            && amount > 0;
    }

    public static string NormalizeMemoryLimit(string? value)
    {
        return IsValidMemoryLimit(value) ? value!.Trim() : DefaultMemoryLimit;
    }

    public static int NormalizeThreads(int value)
    {
        return value >= 1 ? value : DefaultThreads;
    }
}

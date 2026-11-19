namespace Killright.UI.ClipboardMonitoring;

public static class PilotListParser
{
    public static IReadOnlyList<string> ParseIfLikelyPilotList(string text)
    {
        var names = text.Split('\n')
            .Select(x => x.Trim())
            .Where(IsValidEveCharacterName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return names;
    }

    private static bool IsValidEveCharacterName(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return false;

        var value = input.Trim();

        if (value.Length < 3 || value.Length > 37)
            return false;

        if (!IsLetterOrDigit(value[0]) || !IsLetterOrDigit(value[^1]))
            return false;

        if (value.Contains("  ", StringComparison.Ordinal))
            return false;

        return value.All(IsAllowedCharacter);
    }

    private static bool IsAllowedCharacter(char value)
    {
        return IsLetterOrDigit(value) || value == ' ' || value == '\'' || value == '-';
    }

    private static bool IsLetterOrDigit(char value)
    {
        return value is >= 'A' and <= 'Z' || value is >= 'a' and <= 'z' || value is >= '0' and <= '9';
    }
}

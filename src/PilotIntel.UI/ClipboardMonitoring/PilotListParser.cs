namespace PilotIntel.UI.ClipboardMonitoring;

public static class PilotListParser
{
    public static IReadOnlyList<string> ParseIfLikelyPilotList(string text)
    {
        var names = text.Split('\n')
            .Select(x => x.Trim())
            .Where(IsValidEveCharacterName)
            .Distinct(StringComparer.Ordinal)
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

        if (value.Any(character => !IsAllowedCharacter(character)))
            return false;

        var parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length is < 1 or > 3)
            return false;

        if (parts.Any(part => !IsValidNamePart(part)))
            return false;

        var givenName = parts.Length == 1 ? parts[0] : string.Join(' ', parts.Take(parts.Length - 1));

        if (givenName.Length > 24)
            return false;

        if (parts.Length > 1 && parts[^1].Length > 12)
            return false;

        return true;
    }

    private static bool IsValidNamePart(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        return IsLetterOrDigit(value[0]) && IsLetterOrDigit(value[^1]);
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
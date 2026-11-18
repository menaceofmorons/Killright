namespace Killright.Core.Style;

public static class StyleCodes
{
    public const string Unknown = "unknown";
    public const string Inactive = "inactive";
    public const string Victim = "victim";
    public const string Solo = "solo";
    public const string SoloBeginner = "solo_beginner";
    public const string Gang = "gang";
    public const string GangBeginner = "gang_beginner";
    public const string Blob = "blob";
    public const string Fleet = "fleet";

    private static readonly Dictionary<string, StyleClassification> ByCode = new(StringComparer.Ordinal)
    {
        [Unknown] = StyleClassification.Unknown,
        [Inactive] = StyleClassification.Inactive,
        [Victim] = StyleClassification.Victim,
        [SoloBeginner] = StyleClassification.SoloBeginner,
        [Solo] = StyleClassification.Solo,
        [GangBeginner] = StyleClassification.GangBeginner,
        [Gang] = StyleClassification.Gang,
        [Blob] = StyleClassification.Blob,
        [Fleet] = StyleClassification.Fleet
    };

    public static string ToCode(StyleClassification style)
    {
        return style switch
        {
            StyleClassification.Unknown => Unknown,
            StyleClassification.Inactive => Inactive,
            StyleClassification.Victim => Victim,
            StyleClassification.SoloBeginner => SoloBeginner,
            StyleClassification.Solo => Solo,
            StyleClassification.GangBeginner => GangBeginner,
            StyleClassification.Gang => Gang,
            StyleClassification.Blob => Blob,
            StyleClassification.Fleet => Fleet,
            _ => throw new ArgumentOutOfRangeException(nameof(style), style, null)
        };
    }

    public static bool TryFromCode(string? code, out StyleClassification style)
    {
        if (code is not null && ByCode.TryGetValue(code, out style))
            return true;

        style = StyleClassification.Unknown;
        return false;
    }
}

namespace Killright.Core.Style;

public static class StyleLetterCodeFormatter
{
    public static string FormatGeneral(StyleClassification style, bool isPodder)
    {
        var code = style switch
        {
            StyleClassification.Victim => "V",
            StyleClassification.SoloBeginner => "Sb",
            StyleClassification.Solo => "S",
            StyleClassification.GangBeginner => "Gb",
            StyleClassification.Gang => "G",
            StyleClassification.Blob => "B",
            StyleClassification.Fleet => "F",
            _ => "U"
        };

        return AppendPodderSuffix(code, style, isPodder);
    }

    public static string FormatRecent(StyleClassification style, bool isPodder)
    {
        var code = style switch
        {
            StyleClassification.Inactive => "I",
            StyleClassification.Victim => "V",
            StyleClassification.Solo => "S",
            StyleClassification.Gang => "G",
            StyleClassification.Blob => "B",
            StyleClassification.Fleet => "F",
            StyleClassification.Miner => "M",
            StyleClassification.Explorer => "E",
            StyleClassification.Hauler => "H",
            StyleClassification.PI => "P",
            _ => "U"
        };

        return AppendPodderSuffix(code, style, isPodder);
    }

    private static string AppendPodderSuffix(string code, StyleClassification style, bool isPodder)
    {
        if (!isPodder)
            return code;

        return SupportsPodderMarker(style) ? $"{code}x" : code;
    }

    private static bool SupportsPodderMarker(StyleClassification style)
    {
        return style is StyleClassification.Solo
            or StyleClassification.SoloBeginner
            or StyleClassification.Gang
            or StyleClassification.GangBeginner
            or StyleClassification.Blob;
    }
}

using Killright.Core.Style;
using Killright.UI.Resources;

namespace Killright.UI.Style;

public static class StyleLetterCodeFormatter
{
    public static string FormatGeneral(StyleClassification style, bool isPodder)
    {
        var code = style switch
        {
            StyleClassification.Inactive => Strings.Get("Style.LetterCode.Inactive"),
            StyleClassification.Victim => Strings.Get("Style.LetterCode.Victim"),
            StyleClassification.SoloBeginner => Strings.Get("Style.LetterCode.SoloBeginner"),
            StyleClassification.Solo => Strings.Get("Style.LetterCode.Solo"),
            StyleClassification.GangBeginner => Strings.Get("Style.LetterCode.GangBeginner"),
            StyleClassification.Gang => Strings.Get("Style.LetterCode.Gang"),
            StyleClassification.Blob => Strings.Get("Style.LetterCode.Blob"),
            StyleClassification.Fleet => Strings.Get("Style.LetterCode.Fleet"),
            _ => Strings.Get("Style.LetterCode.Unknown")
        };

        return AppendPodderSuffix(code, style, isPodder);
    }

    public static string FormatRecent(StyleClassification style, bool isPodder)
    {
        var code = style switch
        {
            StyleClassification.Inactive => Strings.Get("Style.LetterCode.Inactive"),
            StyleClassification.Victim => Strings.Get("Style.LetterCode.Victim"),
            StyleClassification.Solo => Strings.Get("Style.LetterCode.Solo"),
            StyleClassification.Gang => Strings.Get("Style.LetterCode.Gang"),
            StyleClassification.Blob => Strings.Get("Style.LetterCode.Blob"),
            StyleClassification.Fleet => Strings.Get("Style.LetterCode.Fleet"),
            StyleClassification.Miner => Strings.Get("Style.LetterCode.Miner"),
            StyleClassification.Explorer => Strings.Get("Style.LetterCode.Explorer"),
            StyleClassification.Hauler => Strings.Get("Style.LetterCode.Hauler"),
            StyleClassification.PI => Strings.Get("Style.LetterCode.Pi"),
            _ => Strings.Get("Style.LetterCode.Unknown")
        };

        return AppendPodderSuffix(code, style, isPodder);
    }

    private static string AppendPodderSuffix(string code, StyleClassification style, bool isPodder)
    {
        if (!isPodder)
            return code;

        return SupportsPodderMarker(style) ? $"{code}{Strings.Get("Style.LetterCode.PodderSuffix")}" : code;
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

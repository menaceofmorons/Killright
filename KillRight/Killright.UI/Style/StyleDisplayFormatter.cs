using Killright.Core.Style;
using Killright.UI.Resources;

namespace Killright.UI.Style;

public static class StyleDisplayFormatter
{
    public static string Format(StyleClassification style, bool isPodder = false)
    {
        var word = style switch
        {
            StyleClassification.Inactive => Strings.Get("Style.Word.Inactive"),
            StyleClassification.Victim => Strings.Get("Style.Word.Victim"),
            StyleClassification.SoloBeginner => Strings.Get("Style.Word.SoloBeginner"),
            StyleClassification.Solo => Strings.Get("Style.Word.Solo"),
            StyleClassification.GangBeginner => Strings.Get("Style.Word.GangBeginner"),
            StyleClassification.Gang => Strings.Get("Style.Word.Gang"),
            StyleClassification.Blob => Strings.Get("Style.Word.Blob"),
            StyleClassification.Fleet => Strings.Get("Style.Word.Fleet"),
            _ => Strings.Get("Style.Word.Unknown")
        };

        if (!isPodder || style is not (StyleClassification.Solo or StyleClassification.SoloBeginner
            or StyleClassification.Gang or StyleClassification.GangBeginner or StyleClassification.Blob))
        {
            return word;
        }

        return string.Format(Strings.Get("Style.PodderSuffixFormat"), word);
    }
}

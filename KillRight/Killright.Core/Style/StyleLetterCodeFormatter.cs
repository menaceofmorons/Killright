namespace Killright.Core.Style;

public static class StyleLetterCodeFormatter
{
    public static string FormatGeneral(StyleClassification style)
    {
        return style switch
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
    }

    public static string FormatRecent(StyleClassification style)
    {
        return style switch
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
    }
}

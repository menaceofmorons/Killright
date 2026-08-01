namespace Killright.Core.Style;

public static class StyleDisplayFormatter
{
    public static string Format(StyleClassification style)
    {
        return style switch
        {
            StyleClassification.Inactive => "Inactive",
            StyleClassification.Victim => "Vict",
            StyleClassification.SoloBeginner => "Solo(b)",
            StyleClassification.Solo => "Solo",
            StyleClassification.GangBeginner => "Gang(b)",
            StyleClassification.Gang => "Gang",
            StyleClassification.Blob => "Blob",
            StyleClassification.Fleet => "Fleet",
            StyleClassification.Miner => "Mine",
            StyleClassification.Explorer => "Explo",
            StyleClassification.Hauler => "Haul",
            StyleClassification.PI => "PI",
            _ => "Unk"
        };
    }
}
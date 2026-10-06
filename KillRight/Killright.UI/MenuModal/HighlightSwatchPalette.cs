namespace Killright.UI.MenuModal;

public sealed record HighlightSwatch(string Name, string DarkHex, string LightHex);

public static class HighlightSwatchPalette
{
    public static readonly IReadOnlyList<HighlightSwatch> Swatches = new[]
    {
        new HighlightSwatch("Blue", "#FFADD8E6", "#FF5B9BD5"),
        new HighlightSwatch("Green", "#FF90EE90", "#FF5CB85C"),
        new HighlightSwatch("Purple", "#FFD8B4FE", "#FFA77BDB"),
        new HighlightSwatch("Orange", "#FFFFCC80", "#FFF0A040"),
        new HighlightSwatch("Pink", "#FFF8BBD0", "#FFE57399"),
        new HighlightSwatch("Yellow", "#FFFFF59D", "#FFE0C030"),
        new HighlightSwatch("Teal", "#FFA0E7E5", "#FF3FB5B0"),
        new HighlightSwatch("Gray", "#FFD0D0D0", "#FFA8A8A8")
    };
}

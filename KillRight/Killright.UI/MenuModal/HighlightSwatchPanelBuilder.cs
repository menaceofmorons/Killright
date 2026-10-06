using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Killright.UI.Resources;
using Killright.UI.Theme;
using Killright.UI.UiState;

namespace Killright.UI.MenuModal;

public static class HighlightSwatchPanelBuilder
{
    public static void Build(Panel panel, string? selectedHex, AppTheme resolvedTheme, Action<string> onSelect, Action? onSelectDefault = null)
    {
        panel.Children.Clear();

        if (onSelectDefault is not null)
        {
            var defaultBorder = new Border
            {
                Width = 22,
                Height = 22,
                Margin = new Thickness(0, 0, 6, 6),
                BorderBrush = selectedHex is null ? Brushes.Black : Brushes.Gray,
                BorderThickness = new Thickness(2),
                Cursor = Cursors.Hand,
                ToolTip = UiText.MenuAppearanceHighlightNewPilotDefault,
                Child = new TextBlock
                {
                    Text = "/",
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };

            defaultBorder.SetResourceReference(Border.BackgroundProperty, AppearanceManager.NewPilotBrushKey);
            defaultBorder.MouseLeftButtonDown += (_, _) => onSelectDefault();
            panel.Children.Add(defaultBorder);
        }

        foreach (var swatch in HighlightSwatchPalette.Swatches)
        {
            var border = new Border
            {
                Width = 22,
                Height = 22,
                Margin = new Thickness(0, 0, 6, 6),
                Background = (Brush)new BrushConverter().ConvertFromString(HighlightColorResolver.ResolveHex(swatch.DarkHex, resolvedTheme))!,
                BorderBrush = string.Equals(swatch.DarkHex, selectedHex, StringComparison.OrdinalIgnoreCase) ? Brushes.Black : Brushes.Transparent,
                BorderThickness = new Thickness(2),
                Cursor = Cursors.Hand,
                ToolTip = swatch.Name,
                Tag = swatch.DarkHex
            };

            border.MouseLeftButtonDown += (_, _) => onSelect(swatch.DarkHex);
            panel.Children.Add(border);
        }
    }

    public static void ApplyChip(Border chip, string? selectedHex, AppTheme resolvedTheme)
    {
        if (HighlightColorResolver.TryResolveColor(selectedHex, resolvedTheme, out var color))
            chip.Background = new SolidColorBrush(color);
        else
            chip.SetResourceReference(Border.BackgroundProperty, AppearanceManager.NewPilotBrushKey);
    }
}

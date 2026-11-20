using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Killright.UI.Controls;
using Killright.UI.Configuration;
using Killright.UI.Shortcuts;
using Killright.UI.Theme;
using Killright.UI.UiState;
using Xunit;

namespace Killright.UI.Tests.Shortcuts;

[Collection(SharedApplicationResourcesCollection.Name)]
public sealed class ShortcutsWindowLayoutTests
{
    private const double FallbackInset = 12;
    private const double WindowFrameWidth = 16;
    private const double WindowFrameHeight = 39;

    [Theory]
    [InlineData(AppTheme.Light, 0)]
    [InlineData(AppTheme.Light, 1)]
    [InlineData(AppTheme.Light, 2)]
    [InlineData(AppTheme.Light, 3)]
    [InlineData(AppTheme.Light, 4)]
    [InlineData(AppTheme.Light, 5)]
    [InlineData(AppTheme.Dark, 0)]
    [InlineData(AppTheme.Dark, 1)]
    [InlineData(AppTheme.Dark, 2)]
    [InlineData(AppTheme.Dark, 3)]
    [InlineData(AppTheme.Dark, 4)]
    [InlineData(AppTheme.Dark, 5)]
    public void TabContent_StartsAtTheInset_AndFitsTheViewport(AppTheme theme, int tabIndex)
    {
        Sta.Run(() =>
        {
            var previousSettings = App.Settings;
            var previousTheme = AppearanceManager.ResolvedTheme;
            var previousDictionaries = Application.Current.Resources.MergedDictionaries.ToArray();
            ShortcutsWindow? window = null;

            try
            {
                SetAppSettings(ApplicationSettingsLoader.Load(ApplicationSettingsLoader.GetDefaultSettingsPath()).Settings!);
                AppearanceManager.ApplyTheme(theme);

                window = new ShortcutsWindow();

                var inset = window.Resources["HelpContentInset"] is Thickness configured ? configured.Left : FallbackInset;
                var root = (FrameworkElement)window.Content;
                var tabs = Descendants<TabControl>(root).Single();

                tabs.SelectedIndex = tabIndex;
                Layout(root, window);

                var host = Descendants<Border>(tabs).First(border => border.Parent is Grid && Grid.GetRow(border) == 1);
                var scroller = Descendants<ScrollViewer>(host).FirstOrDefault();
                var viewportWidth = scroller is null ? host.ActualWidth : scroller.ViewportWidth;
                var scrollbarShown = scroller?.ComputedVerticalScrollBarVisibility == Visibility.Visible;
                var spare = scrollbarShown ? 0 : SystemParameters.VerticalScrollBarWidth;
                var elements = Descendants<FrameworkElement>(host).Where(IsContentElement).ToArray();

                Assert.NotEmpty(elements);

                var first = elements[0].TransformToAncestor(host).Transform(new Point(0, 0)).X;

                Assert.True(first >= inset, $"tab {tabIndex}: first content starts at {first}, inset {inset}");

                var reference = scroller ?? (FrameworkElement)host;

                foreach (var element in elements)
                {
                    var right = element.TransformToAncestor(reference).Transform(new Point(element.ActualWidth, 0)).X;

                    var limit = viewportWidth - inset - (double.IsNaN(element.Width) ? 0 : spare);

                    Assert.True(right <= limit, $"tab {tabIndex}: {element.GetType().Name} right edge {right} beyond {limit}");
                }
            }
            finally
            {
                window?.Close();
                AppearanceManager.ApplyTheme(previousTheme);
                SetAppSettings(previousSettings);

                var merged = Application.Current.Resources.MergedDictionaries;
                merged.Clear();

                foreach (var dictionary in previousDictionaries)
                    merged.Add(dictionary);
            }
        });
    }

    private static bool IsContentElement(FrameworkElement element) =>
        element is TextBlock { Text.Length: > 0 } or ThreatGlyph or ConfidenceGlyph;

    private static void Layout(FrameworkElement root, ShortcutsWindow window)
    {
        var size = new Size(window.Width - WindowFrameWidth, window.Height - WindowFrameHeight);

        root.Measure(size);
        root.Arrange(new Rect(size));
        root.UpdateLayout();
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root)
        where T : DependencyObject
    {
        var count = VisualTreeHelper.GetChildrenCount(root);

        for (var index = 0; index < count; index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);

            if (child is T match)
                yield return match;

            foreach (var nested in Descendants<T>(child))
                yield return nested;
        }
    }

    private static void SetAppSettings(ApplicationSettings? settings) =>
        typeof(App).GetProperty(nameof(App.Settings), System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)!.GetSetMethod(nonPublic: true)!.Invoke(null, new object?[] { settings });
}

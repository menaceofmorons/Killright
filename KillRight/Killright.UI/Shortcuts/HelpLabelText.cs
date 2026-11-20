using System.Windows;
using System.Windows.Controls;

namespace Killright.UI.Shortcuts;

public static class HelpLabelText
{
    private const char LabelTerminator = ':';

    public static readonly DependencyProperty TextProperty = DependencyProperty.RegisterAttached(
        "Text",
        typeof(string),
        typeof(HelpLabelText),
        new PropertyMetadata(string.Empty, OnTextChanged));

    public static string GetText(DependencyObject element) => (string)element.GetValue(TextProperty);

    public static void SetText(DependencyObject element, string value) => element.SetValue(TextProperty, value);

    public static (string Label, string Remainder) Split(string text)
    {
        var index = text.IndexOf(LabelTerminator);

        return index < 0 ? (string.Empty, text) : (text[..(index + 1)], text[(index + 1)..]);
    }

    private static void OnTextChanged(DependencyObject element, DependencyPropertyChangedEventArgs args)
    {
        if (element is not TextBlock block)
            return;

        var (label, remainder) = Split(args.NewValue as string ?? string.Empty);

        block.Inlines.Clear();

        if (label.Length > 0)
            block.Inlines.Add(new System.Windows.Documents.Run(label) { FontWeight = FontWeights.Bold });

        block.Inlines.Add(new System.Windows.Documents.Run(remainder));
    }
}

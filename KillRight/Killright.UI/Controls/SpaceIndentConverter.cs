using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace Killright.UI.Controls;

public sealed class SpaceIndentConverter : IMultiValueConverter
{
    public static double SpaceWidth(double fontSize, FontFamily fontFamily)
    {
        var typeface = new Typeface(fontFamily, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        var text = new FormattedText(" ", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface, fontSize, Brushes.Black, 1.0);
        return text.WidthIncludingTrailingWhitespace;
    }

    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 2 || values[0] is not double fontSize || values[1] is not FontFamily fontFamily)
            return new Thickness(0);

        return new Thickness(SpaceWidth(fontSize, fontFamily), 0, 0, 0);
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

using System.Windows;

namespace Killright.UI.InfoSheet;

public static class InfoSheetPlacement
{
    public static Point Calculate(double cursorX, Rect row, Size sheet, Rect visibleArea)
    {
        var left = Math.Min(cursorX, visibleArea.Right - sheet.Width);
        left = Math.Max(left, visibleArea.Left);

        var below = row.Bottom;
        var above = row.Top - sheet.Height;

        double top;

        if (below + sheet.Height <= visibleArea.Bottom)
            top = below;
        else if (above >= visibleArea.Top)
            top = above;
        else
            top = Math.Max(visibleArea.Top, visibleArea.Bottom - sheet.Height);

        return new Point(left, top);
    }
}

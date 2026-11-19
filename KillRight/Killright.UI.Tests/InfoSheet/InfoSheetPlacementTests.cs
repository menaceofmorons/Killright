using System.Windows;
using Killright.UI.InfoSheet;
using Xunit;

namespace Killright.UI.Tests.InfoSheet;

public sealed class InfoSheetPlacementTests
{
    private static readonly Rect Screen = new(0, 0, 1920, 1080);
    private static readonly Size Sheet = new(240, 300);

    [Fact]
    public void RoomBelow_TopEdgeIsAtRowBottomAndLeftIsCursor()
    {
        var row = new Rect(100, 200, 800, 20);

        var position = InfoSheetPlacement.Calculate(350, row, Sheet, Screen);

        Assert.Equal(350, position.X);
        Assert.True(position.Y >= row.Bottom);
    }

    [Fact]
    public void NoRoomBelow_PlacedAboveRowWithoutCoveringIt()
    {
        var row = new Rect(100, 900, 800, 20);

        var position = InfoSheetPlacement.Calculate(350, row, Sheet, Screen);

        Assert.True(position.Y + Sheet.Height <= row.Top);
        Assert.True(position.Y >= Screen.Top);
    }

    [Fact]
    public void CursorNearRightEdge_SheetStaysOnScreen()
    {
        var row = new Rect(100, 200, 1800, 20);

        var position = InfoSheetPlacement.Calculate(1900, row, Sheet, Screen);

        Assert.True(position.X + Sheet.Width <= Screen.Right);
        Assert.True(position.X >= Screen.Left);
    }

    [Fact]
    public void NoRoomAboveOrBelow_ClampsInsideVisibleArea()
    {
        var area = new Rect(0, 0, 800, 320);
        var row = new Rect(0, 150, 800, 20);

        var position = InfoSheetPlacement.Calculate(10, row, Sheet, area);

        Assert.True(position.Y >= area.Top);
        Assert.True(position.Y + Sheet.Height <= area.Bottom);
    }
}

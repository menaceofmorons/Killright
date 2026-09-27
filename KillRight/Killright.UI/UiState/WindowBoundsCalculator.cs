namespace Killright.UI.UiState;

public static class WindowBoundsCalculator
{
    public static (double Left, double Top) CenterOn(
        double workAreaLeft,
        double workAreaTop,
        double workAreaWidth,
        double workAreaHeight,
        double windowWidth,
        double windowHeight)
    {
        var left = workAreaLeft + ((workAreaWidth - windowWidth) / 2);
        var top = workAreaTop + ((workAreaHeight - windowHeight) / 2);
        return (left, top);
    }

    public static (double Left, double Top) ClampToVirtualScreen(
        double left,
        double top,
        double windowWidth,
        double windowHeight,
        double virtualScreenLeft,
        double virtualScreenTop,
        double virtualScreenWidth,
        double virtualScreenHeight)
    {
        var maxLeft = virtualScreenLeft + Math.Max(0, virtualScreenWidth - windowWidth);
        var maxTop = virtualScreenTop + Math.Max(0, virtualScreenHeight - windowHeight);
        var clampedLeft = Math.Min(Math.Max(left, virtualScreenLeft), maxLeft);
        var clampedTop = Math.Min(Math.Max(top, virtualScreenTop), maxTop);
        return (clampedLeft, clampedTop);
    }
}

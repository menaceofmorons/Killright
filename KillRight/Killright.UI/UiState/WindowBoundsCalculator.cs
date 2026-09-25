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
}

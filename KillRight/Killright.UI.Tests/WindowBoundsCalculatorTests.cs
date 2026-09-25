using Killright.UI.UiState;
using Xunit;

namespace Killright.UI.Tests;

public class WindowBoundsCalculatorTests
{
    [Fact]
    public void CenterOn_CentersWithinWorkArea()
    {
        var (left, top) = WindowBoundsCalculator.CenterOn(
            workAreaLeft: 0,
            workAreaTop: 0,
            workAreaWidth: 1920,
            workAreaHeight: 1080,
            windowWidth: 960,
            windowHeight: 480);

        Assert.Equal(480, left);
        Assert.Equal(300, top);
    }

    [Fact]
    public void CenterOn_OffsetWorkArea_AccountsForOrigin()
    {
        var (left, top) = WindowBoundsCalculator.CenterOn(
            workAreaLeft: 1920,
            workAreaTop: 0,
            workAreaWidth: 1280,
            workAreaHeight: 1024,
            windowWidth: 960,
            windowHeight: 480);

        Assert.Equal(1920 + 160, left);
        Assert.Equal(272, top);
    }
}

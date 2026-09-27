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

    [Fact]
    public void ClampToVirtualScreen_WithinBounds_Unchanged()
    {
        var (left, top) = WindowBoundsCalculator.ClampToVirtualScreen(
            left: 100,
            top: 200,
            windowWidth: 720,
            windowHeight: 360,
            virtualScreenLeft: 0,
            virtualScreenTop: 0,
            virtualScreenWidth: 1920,
            virtualScreenHeight: 1080);

        Assert.Equal(100, left);
        Assert.Equal(200, top);
    }

    [Fact]
    public void ClampToVirtualScreen_PositionOffDisconnectedMonitor_ClampsIntoVirtualScreen()
    {
        var (left, top) = WindowBoundsCalculator.ClampToVirtualScreen(
            left: 3000,
            top: -500,
            windowWidth: 720,
            windowHeight: 360,
            virtualScreenLeft: 0,
            virtualScreenTop: 0,
            virtualScreenWidth: 1920,
            virtualScreenHeight: 1080);

        Assert.Equal(1200, left);
        Assert.Equal(0, top);
    }

    [Fact]
    public void ClampToVirtualScreen_WindowLargerThanVirtualScreen_AnchorsToOrigin()
    {
        var (left, top) = WindowBoundsCalculator.ClampToVirtualScreen(
            left: 100,
            top: 100,
            windowWidth: 2000,
            windowHeight: 1200,
            virtualScreenLeft: 0,
            virtualScreenTop: 0,
            virtualScreenWidth: 1920,
            virtualScreenHeight: 1080);

        Assert.Equal(0, left);
        Assert.Equal(0, top);
    }
}

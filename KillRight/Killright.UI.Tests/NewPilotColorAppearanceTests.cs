using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Killright.UI.Controls;
using Killright.UI.Theme;
using Xunit;

namespace Killright.UI.Tests;

public sealed class NewPilotColorAppearanceTests
{
    private static void RunSta(Action action)
    {
        Exception? failure = null;

        var thread = new Thread(() =>
        {
            try
            {
                lock (Sta.Gate)
                {
                    if (Application.Current is null)
                        _ = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                }

                action();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (failure is not null)
            throw new Xunit.Sdk.XunitException(failure.ToString());
    }

    [Fact]
    public void ApplyNewPilotColor_ChosenColour_SetsBrushWithHighlightOpacity()
    {
        RunSta(() =>
        {
            AppearanceManager.ApplyNewPilotColor("#FFD8B4FE", 0.2);

            var brush = Assert.IsType<SolidColorBrush>(Application.Current.Resources[AppearanceManager.NewPilotBrushKey]);
            Assert.Equal(Color.FromArgb(51, 0xD8, 0xB4, 0xFE), brush.Color);

            AppearanceManager.ApplyNewPilotColor(null, 0.2);
        });
    }

    [Fact]
    public void ApplyNewPilotColor_NullOrInvalidHex_RemovesOverrideSoThemeBrushApplies()
    {
        RunSta(() =>
        {
            AppearanceManager.ApplyNewPilotColor("#FFD8B4FE", 0.2);
            AppearanceManager.ApplyNewPilotColor(null, 0.2);
            Assert.False(Application.Current.Resources.Contains(AppearanceManager.NewPilotBrushKey));

            AppearanceManager.ApplyNewPilotColor("#FFD8B4FE", 0.2);
            AppearanceManager.ApplyNewPilotColor("not-a-colour", 0.2);
            Assert.False(Application.Current.Resources.Contains(AppearanceManager.NewPilotBrushKey));
        });
    }

    [Fact]
    public void ApplyNewPilotColor_RevertToCommittedValue_RestoresPreviousOverride()
    {
        RunSta(() =>
        {
            AppearanceManager.ApplyNewPilotColor("#FFADD8E6", 0.2);
            AppearanceManager.ApplyNewPilotColor("#FFF8BBD0", 0.2);
            AppearanceManager.ApplyNewPilotColor("#FFADD8E6", 0.2);

            var brush = Assert.IsType<SolidColorBrush>(Application.Current.Resources[AppearanceManager.NewPilotBrushKey]);
            Assert.Equal(Color.FromArgb(51, 0xAD, 0xD8, 0xE6), brush.Color);

            AppearanceManager.ApplyNewPilotColor(null, 0.2);
        });
    }

    [Fact]
    public void SpaceWidth_IsPositiveAndScalesWithFontSize()
    {
        var family = new FontFamily("Segoe UI");

        var small = SpaceIndentConverter.SpaceWidth(10, family);
        var large = SpaceIndentConverter.SpaceWidth(20, family);

        Assert.True(small > 0);
        Assert.Equal(small * 2, large, 3);
    }

    [Fact]
    public void Convert_ReturnsLeftOnlyThicknessOfOneSpace()
    {
        var family = new FontFamily("Segoe UI");

        var result = Assert.IsType<Thickness>(
            new SpaceIndentConverter().Convert(new object[] { 13d, family }, typeof(Thickness), null!, CultureInfo.InvariantCulture));

        Assert.Equal(SpaceIndentConverter.SpaceWidth(13, family), result.Left, 3);
        Assert.Equal(0, result.Top);
        Assert.Equal(0, result.Right);
        Assert.Equal(0, result.Bottom);
    }
}

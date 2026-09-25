using Killright.UI.UiState;
using Xunit;

namespace Killright.UI.Tests;

public class UiStateLoaderTests
{
    [Fact]
    public void LoadOrDefault_MissingFile_ReturnsDefaultsNotCorrupt()
    {
        var path = Path.Combine(Path.GetTempPath(), $"ui-state-{Guid.NewGuid():N}.json");

        var result = UiStateLoader.LoadOrDefault(path);

        Assert.False(result.FileExisted);
        Assert.False(result.WasCorrupt);
        Assert.Equal(UiStateDefaults.WindowWidth, result.State.WindowWidth);
        Assert.Equal(UiStateDefaults.WindowHeight, result.State.WindowHeight);
        Assert.Equal(UiStateDefaults.AlwaysOnTop, result.State.AlwaysOnTop);
    }

    [Fact]
    public void LoadOrDefault_CorruptFile_ReturnsDefaultsAndFlagsCorrupt()
    {
        var path = Path.Combine(Path.GetTempPath(), $"ui-state-{Guid.NewGuid():N}.json");

        try
        {
            File.WriteAllText(path, "{ not valid json");

            var result = UiStateLoader.LoadOrDefault(path);

            Assert.True(result.FileExisted);
            Assert.True(result.WasCorrupt);
            Assert.Equal(UiStateDefaults.WindowWidth, result.State.WindowWidth);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void SaveThenLoad_RoundTripsValues()
    {
        var path = Path.Combine(Path.GetTempPath(), $"ui-state-{Guid.NewGuid():N}.json");

        try
        {
            var state = new UiStateModel
            {
                WindowLeft = 123,
                WindowTop = 456,
                WindowWidth = 1024,
                WindowHeight = 640,
                AlwaysOnTop = false
            };

            UiStateLoader.Save(state, path);
            var result = UiStateLoader.LoadOrDefault(path);

            Assert.True(result.FileExisted);
            Assert.False(result.WasCorrupt);
            Assert.Equal(state.WindowLeft, result.State.WindowLeft);
            Assert.Equal(state.WindowTop, result.State.WindowTop);
            Assert.Equal(state.WindowWidth, result.State.WindowWidth);
            Assert.Equal(state.WindowHeight, result.State.WindowHeight);
            Assert.Equal(state.AlwaysOnTop, result.State.AlwaysOnTop);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void LoadOrDefault_UnknownFutureFields_AreToleratedAndIgnored()
    {
        var path = Path.Combine(Path.GetTempPath(), $"ui-state-{Guid.NewGuid():N}.json");

        try
        {
            File.WriteAllText(path, """
                {
                  "version": 1,
                  "windowLeft": 10,
                  "windowTop": 20,
                  "windowWidth": 900,
                  "windowHeight": 500,
                  "alwaysOnTop": true,
                  "someFutureField": "value from a later row"
                }
                """);

            var result = UiStateLoader.LoadOrDefault(path);

            Assert.False(result.WasCorrupt);
            Assert.Equal(10, result.State.WindowLeft);
            Assert.Equal(900, result.State.WindowWidth);
        }
        finally
        {
            File.Delete(path);
        }
    }
}

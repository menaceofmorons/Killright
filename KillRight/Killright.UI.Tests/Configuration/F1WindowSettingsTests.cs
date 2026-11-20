using System.IO;
using Killright.UI.Configuration;
using Xunit;

namespace Killright.UI.Tests.Configuration;

public sealed class F1WindowSettingsTests
{
    [Fact]
    public void Load_RealSettingsFile_ReadsRecordedF1Size()
    {
        var f1 = ApplicationSettingsLoader.Load(ApplicationSettingsLoader.GetDefaultSettingsPath()).Settings!.Window.F1;

        Assert.Equal(425, f1.Width);
        Assert.Equal(424, f1.Height);
    }

    [Fact]
    public void Load_BothKeysPresent_AreRead()
    {
        var log = new List<string>();

        var f1 = LoadJson("{ \"window\": { \"f1\": { \"width\": 500, \"height\": 600 } } }", log).Window.F1;

        Assert.Equal(500, f1.Width);
        Assert.Equal(600, f1.Height);
        Assert.DoesNotContain(log, line => line.Contains("window.f1"));
    }

    [Fact]
    public void Load_SectionMissing_UsesDefaultsAndLogsEachKey()
    {
        var log = new List<string>();

        var f1 = LoadJson("{ \"recentWindowDays\": 14 }", log).Window.F1;

        Assert.Equal(425, f1.Width);
        Assert.Equal(424, f1.Height);
        Assert.Contains(log, line => line.Contains("'window.f1.width'"));
        Assert.Contains(log, line => line.Contains("'window.f1.height'"));
    }

    [Theory]
    [InlineData("10")]
    [InlineData("0")]
    [InlineData("-5")]
    public void Load_InvalidWidth_LogsAndUsesDefaultWhileHeightIsKept(string width)
    {
        var log = new List<string>();

        var f1 = LoadJson($"{{ \"window\": {{ \"f1\": {{ \"width\": {width}, \"height\": 500 }} }} }}", log).Window.F1;

        Assert.Equal(425, f1.Width);
        Assert.Equal(500, f1.Height);
        Assert.Contains(log, line => line.Contains("'window.f1.width'"));
    }

    [Fact]
    public void ShortcutsWindow_TakesSizeFromSettings()
    {
        var source = File.ReadAllText(FindSource("Shortcuts", "ShortcutsWindow.xaml.cs"));

        Assert.Contains("Width = App.Settings.Window.F1.Width;", source);
        Assert.Contains("Height = App.Settings.Window.F1.Height;", source);
    }

    [Fact]
    public void NoTempSizingCodeRemains()
    {
        var root = Path.GetDirectoryName(FindSource("Shortcuts", "ShortcutsWindow.xaml.cs"))!;

        Assert.False(File.Exists(Path.Combine(root, "F1SizeRecorder.cs")));
        Assert.DoesNotContain("F1SizeRecorder", File.ReadAllText(FindSource("Shortcuts", "ShortcutsWindow.xaml.cs")));
        Assert.DoesNotContain("TEMP:", File.ReadAllText(FindSource("Shortcuts", "ShortcutsWindow.xaml.cs")));
        Assert.DoesNotContain("Shortcuts.Sizing", File.ReadAllText(FindSource("Resources", "Strings.resx")));
    }

    private static ApplicationSettings LoadJson(string json, List<string> log)
    {
        var path = Path.Combine(Path.GetTempPath(), $"killright-settings-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, json);

        try
        {
            return ApplicationSettingsLoader.Load(path, log.Add).Settings!;
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static string FindSource(string folder, string file)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "Killright.UI", folder, file);

            if (File.Exists(candidate))
                return candidate;

            directory = directory.Parent;
        }

        throw new FileNotFoundException(file);
    }
}

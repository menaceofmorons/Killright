using Xunit;

namespace Killright.UI.Tests.Diagnostics;

public sealed class DiscardedTaskSourceTests
{
    [Fact]
    public void App_SubscribesToUnobservedTaskExceptions()
    {
        Assert.Contains("TaskScheduler.UnobservedTaskException +=", ReadUiSource("App.xaml.cs"));
    }

    [Fact]
    public void App_DoesNotDiscardTheSdeCheckOrStartupPurgeUnguarded()
    {
        var source = ReadUiSource("App.xaml.cs");

        Assert.DoesNotContain("_ = Task.Run(() => sdeIngestionService.RunCheckWithRetryAsync());", source);
        Assert.DoesNotContain("_ = PurgeScheduler.RunStartupPassAsync();", source);
    }

    [Fact]
    public void LoadInfoSheetAsync_RecordsItsFailure()
    {
        var source = ReadUiSource("MainWindow.xaml.cs");
        var start = source.IndexOf("Task LoadInfoSheetAsync(", StringComparison.Ordinal);

        Assert.True(start >= 0);
        Assert.Contains("EngineFailureLog.Record", source.Substring(start, 400));
    }

    private static string ReadUiSource(string fileName)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "KillRight.sln")))
            directory = directory.Parent;

        Assert.NotNull(directory);

        return File.ReadAllText(Path.Combine(directory!.FullName, "Killright.UI", fileName)).Replace("\r\n", "\n");
    }
}

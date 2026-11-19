using System.IO;
using Killright.UI.Shortcuts;
using Xunit;

namespace Killright.UI.Tests.Shortcuts;

public sealed class F1SizeRecorderTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "kr-f1-size-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(directory))
            Directory.Delete(directory, recursive: true);
    }

    [Fact]
    public void Record_WritesWidthAndHeight()
    {
        var path = Path.Combine(directory, "size.txt");

        var error = F1SizeRecorder.Record(512.4, 700.6, path);

        Assert.Null(error);
        Assert.Equal(new[] { "width=512", "height=701" }, File.ReadAllLines(path));
    }

    [Fact]
    public void Record_UnwritableTarget_ReturnsErrorWithoutThrowing()
    {
        Directory.CreateDirectory(directory);

        var error = F1SizeRecorder.Record(500, 600, directory);

        Assert.False(string.IsNullOrEmpty(error));
    }

    [Fact]
    public void CancelHandler_DoesNotRecord()
    {
        var source = File.ReadAllText(FindSource("ShortcutsWindow.xaml.cs"));
        var cancel = source[source.IndexOf("Cancel_Click", StringComparison.Ordinal)..];

        Assert.DoesNotContain("Record(", cancel);
    }

    private static string FindSource(string fileName)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "KillRight.sln")))
            dir = dir.Parent;

        return Path.Combine(dir!.FullName, "Killright.UI", "Shortcuts", fileName);
    }
}

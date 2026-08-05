using System.IO;
using Killright.Storage.GroupHistory;
using Xunit;

namespace Killright.Storage.Tests;

public sealed class HistoryUpdaterExecutableLocatorNegativeTests
{
    [Fact]
    public void Locate_ReturnsNull_WhenSolutionFileCannotBeFound()
    {
        var root = Path.Combine(Path.GetTempPath(), $"no-solution-{Guid.NewGuid():N}");
        var startDirectory = Path.Combine(root, "some", "nested", "directory");
        Directory.CreateDirectory(startDirectory);

        try
        {
            var located = HistoryUpdaterExecutableLocator.Locate(startDirectory);

            Assert.Null(located);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Locate_ReturnsNull_WhenExecutableIsMissingFromBothTargetDirectories()
    {
        var root = Path.Combine(Path.GetTempPath(), $"killright-repo-{Guid.NewGuid():N}");
        var killRightDirectory = Path.Combine(root, "KillRight");
        var startDirectory = Path.Combine(killRightDirectory, "Killright.UI", "bin", "Debug", "net9.0-windows");
        Directory.CreateDirectory(startDirectory);
        File.WriteAllText(Path.Combine(killRightDirectory, "KillRight.sln"), string.Empty);
        Directory.CreateDirectory(Path.Combine(root, "killright_history_updater", "target"));

        try
        {
            var located = HistoryUpdaterExecutableLocator.Locate(startDirectory);

            Assert.Null(located);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}

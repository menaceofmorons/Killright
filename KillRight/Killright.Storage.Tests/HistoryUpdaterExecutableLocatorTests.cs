using System.IO;
using Killright.Storage.GroupHistory;
using Xunit;

namespace Killright.Storage.Tests;

public sealed class HistoryUpdaterExecutableLocatorTests
{
    [Fact]
    public void Locate_FindsExecutable_InReleaseTarget()
    {
        var root = CreateTemporaryRepositoryLayout(createReleaseExecutable: true, createDebugExecutable: false);

        try
        {
            var startDirectory = Path.Combine(root, "KillRight", "Killright.UI", "bin", "Debug", "net9.0-windows");
            Directory.CreateDirectory(startDirectory);

            var located = HistoryUpdaterExecutableLocator.Locate(startDirectory);

            Assert.NotNull(located);
            Assert.Equal(
                Path.Combine(root, "killright_history_updater", "target", "release", HistoryUpdaterExecutableLocator.ExecutableFileName),
                located);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Locate_FindsExecutable_InDebugTarget_WhenReleaseMissing()
    {
        var root = CreateTemporaryRepositoryLayout(createReleaseExecutable: false, createDebugExecutable: true);

        try
        {
            var startDirectory = Path.Combine(root, "KillRight", "Killright.UI", "bin", "Debug", "net9.0-windows");
            Directory.CreateDirectory(startDirectory);

            var located = HistoryUpdaterExecutableLocator.Locate(startDirectory);

            Assert.NotNull(located);
            Assert.Equal(
                Path.Combine(root, "killright_history_updater", "target", "debug", HistoryUpdaterExecutableLocator.ExecutableFileName),
                located);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Locate_PrefersRelease_WhenBothTargetsHaveTheExecutable()
    {
        var root = CreateTemporaryRepositoryLayout(createReleaseExecutable: true, createDebugExecutable: true);

        try
        {
            var startDirectory = Path.Combine(root, "KillRight", "Killright.UI", "bin", "Debug", "net9.0-windows");
            Directory.CreateDirectory(startDirectory);

            var located = HistoryUpdaterExecutableLocator.Locate(startDirectory);

            Assert.NotNull(located);
            Assert.Contains(Path.Combine("target", "release"), located);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Locate_FindsRealExecutable_UsingThisTestAssemblysOwnRepositoryLayout()
    {
        var located = HistoryUpdaterExecutableLocator.Locate();

        Assert.True(
            located is not null && File.Exists(located),
            "Expected killright_history_updater.exe to be found under Code/killright_history_updater/target. Run 'cargo build' in Code/killright_history_updater first (Build Verification 4.1).");
    }

    private static string CreateTemporaryRepositoryLayout(bool createReleaseExecutable, bool createDebugExecutable)
    {
        var root = Path.Combine(Path.GetTempPath(), $"killright-repo-{Guid.NewGuid():N}");
        var killRightDirectory = Path.Combine(root, "KillRight");
        Directory.CreateDirectory(killRightDirectory);
        File.WriteAllText(Path.Combine(killRightDirectory, "KillRight.sln"), string.Empty);

        if (createReleaseExecutable)
        {
            var releaseDirectory = Path.Combine(root, "killright_history_updater", "target", "release");
            Directory.CreateDirectory(releaseDirectory);
            File.WriteAllText(Path.Combine(releaseDirectory, HistoryUpdaterExecutableLocator.ExecutableFileName), string.Empty);
        }

        if (createDebugExecutable)
        {
            var debugDirectory = Path.Combine(root, "killright_history_updater", "target", "debug");
            Directory.CreateDirectory(debugDirectory);
            File.WriteAllText(Path.Combine(debugDirectory, HistoryUpdaterExecutableLocator.ExecutableFileName), string.Empty);
        }

        return root;
    }
}

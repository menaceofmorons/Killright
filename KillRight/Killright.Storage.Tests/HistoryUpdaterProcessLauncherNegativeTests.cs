using System.IO;
using Killright.Storage.GroupHistory;
using Xunit;

namespace Killright.Storage.Tests;

public sealed class HistoryUpdaterProcessLauncherNegativeTests
{
    [Fact]
    public void LaunchDetached_ExecutablePathDoesNotExist_Throws()
    {
        var missingExecutablePath = Path.Combine(Path.GetTempPath(), $"does-not-exist-{Guid.NewGuid():N}.exe");

        Assert.ThrowsAny<Exception>(() => HistoryUpdaterProcessLauncher.LaunchDetached(missingExecutablePath, "build-staging"));
    }
}

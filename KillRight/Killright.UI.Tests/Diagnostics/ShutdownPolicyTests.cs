using Killright.UI.Diagnostics;
using Xunit;

namespace Killright.UI.Tests.Diagnostics;

public sealed class ShutdownPolicyTests
{
    [Theory]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, false)]
    public void ShouldBackup_FollowsRefusalAndSkipSetting(bool startupRefused, bool skipBackupOnClose, bool expected)
    {
        Assert.Equal(expected, ShutdownPolicy.ShouldBackup(startupRefused, skipBackupOnClose));
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void ShouldCheckpoint_SkippedWhenStartupWasRefused(bool startupRefused, bool expected)
    {
        Assert.Equal(expected, ShutdownPolicy.ShouldCheckpoint(startupRefused));
    }
}

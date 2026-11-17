namespace Killright.UI.Diagnostics;

public static class ShutdownPolicy
{
    public static bool ShouldBackup(bool startupRefused, bool skipBackupOnClose)
    {
        return !startupRefused && !skipBackupOnClose;
    }

    public static bool ShouldCheckpoint(bool startupRefused)
    {
        return !startupRefused;
    }
}

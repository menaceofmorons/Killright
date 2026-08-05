namespace Killright.Storage.GroupHistory;

public static class GroupHistoryLiveConfigPaths
{
    public const string LiveStatusFileName = "groupHistory.status.json";
    public const string SwapSignalFileName = "database.new";

    public static string GetLiveConfigDirectory()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var directory = Path.Combine(localAppData, "KillRight", "config");
        Directory.CreateDirectory(directory);
        return directory;
    }

    public static string GetLiveStatusPath()
    {
        return Path.Combine(GetLiveConfigDirectory(), LiveStatusFileName);
    }

    public static string GetSwapSignalPath()
    {
        return Path.Combine(GetLiveConfigDirectory(), SwapSignalFileName);
    }
}

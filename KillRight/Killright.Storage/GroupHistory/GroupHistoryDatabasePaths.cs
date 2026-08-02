namespace Killright.Storage.GroupHistory;

public static class GroupHistoryDatabasePaths
{
    public static string GetDefaultDatabasePath()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var directory = Path.Combine(localAppData, "KillRight", "GroupHistory");
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, GroupHistoryConstants.HistoricDatabaseFileName);
    }
}
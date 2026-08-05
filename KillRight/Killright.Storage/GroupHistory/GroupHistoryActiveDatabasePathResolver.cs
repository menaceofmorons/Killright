namespace Killright.Storage.GroupHistory;

public sealed class GroupHistoryActiveDatabasePathResolver
{
    private readonly object _lock = new();
    private string _currentDatabasePath;

    public GroupHistoryActiveDatabasePathResolver(string? defaultDatabasePath = null)
    {
        _currentDatabasePath = defaultDatabasePath ?? GroupHistoryDatabasePaths.GetDefaultDatabasePath();
    }

    public string GetCurrentDatabasePath()
    {
        lock (_lock)
        {
            return _currentDatabasePath;
        }
    }

    public bool Repoint(string? activeDatabaseFile)
    {
        if (string.IsNullOrWhiteSpace(activeDatabaseFile))
            return false;

        if (!File.Exists(activeDatabaseFile))
            return false;

        lock (_lock)
        {
            _currentDatabasePath = activeDatabaseFile;
        }

        return true;
    }
}

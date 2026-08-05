namespace Killright.Storage.GroupHistory;

public sealed class GroupHistorySwapWatcher
{
    private readonly GroupHistoryActiveDatabasePathResolver _resolver;
    private readonly string _swapSignalPath;
    private readonly string? _liveStatusPath;

    public GroupHistorySwapWatcher(
        GroupHistoryActiveDatabasePathResolver resolver,
        string? swapSignalPath = null,
        string? liveStatusPath = null)
    {
        _resolver = resolver;
        _swapSignalPath = swapSignalPath ?? GroupHistoryLiveConfigPaths.GetSwapSignalPath();
        _liveStatusPath = liveStatusPath;
    }

    public bool CheckAndApply()
    {
        if (!File.Exists(_swapSignalPath))
            return false;

        File.Delete(_swapSignalPath);

        var status = GroupHistoryLiveStatusLoader.LoadOrDefault(_liveStatusPath);
        return _resolver.Repoint(status.ActiveDatabaseFile);
    }
}

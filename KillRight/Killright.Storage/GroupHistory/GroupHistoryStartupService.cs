using Killright.Storage.GroupHistory.Models;

namespace Killright.Storage.GroupHistory;

public sealed class GroupHistoryStartupService
{
    private readonly IGroupHistoryDatabase _database;

    public GroupHistoryStartupService(IGroupHistoryDatabase database)
    {
        _database = database;
    }

    public Task<GroupHistoryUpdateRequirement> GetStartupRequirementAsync(CancellationToken cancellationToken = default)
    {
        return _database.GetUpdateRequirementAsync(DateTime.UtcNow, cancellationToken);
    }
}
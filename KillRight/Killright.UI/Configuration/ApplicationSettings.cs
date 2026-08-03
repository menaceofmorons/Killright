using Killright.Storage.GroupHistory.Models;

namespace Killright.UI.Configuration;

public sealed class ApplicationSettings
{
    public GroupHistoryApplicationSettings GroupHistory { get; init; } = new();
}

public sealed class GroupHistoryApplicationSettings
{
    public int ImportBatchSize { get; init; } = GroupHistoryImportBatchOptions.DefaultBatchSize;

    public GroupHistoryImportBatchOptions ToBatchOptions()
    {
        return GroupHistoryImportBatchOptions.FromSingleBatchSize(ImportBatchSize);
    }
}
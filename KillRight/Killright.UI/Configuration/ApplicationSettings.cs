using Killright.Integration.zKill.History;
using Killright.Storage.GroupHistory.Models;

namespace Killright.UI.Configuration;

public sealed class ApplicationSettings
{
    public GroupHistoryApplicationSettings GroupHistory { get; init; } = new();
}

public sealed class GroupHistoryApplicationSettings
{
    public int ImportBatchSize { get; init; } = GroupHistoryImportBatchOptions.DefaultBatchSize;

    public int ParallelDownloadWorkers { get; init; } = ZkillHistoryParallelDownloadOptions.DefaultParallelDownloadWorkers;

    public int ZkillDocumentedMaxRequestsPerSecond { get; init; } = ZkillHistoryParallelDownloadOptions.DefaultDocumentedMaxRequestsPerSecond;

    public GroupHistoryImportBatchOptions ToBatchOptions()
    {
        return GroupHistoryImportBatchOptions.FromSingleBatchSize(ImportBatchSize);
    }

    public ZkillHistoryParallelDownloadOptions ToParallelDownloadOptions()
    {
        return ZkillHistoryParallelDownloadOptions.FromConfiguredValues(
            ParallelDownloadWorkers,
            ZkillDocumentedMaxRequestsPerSecond);
    }
}

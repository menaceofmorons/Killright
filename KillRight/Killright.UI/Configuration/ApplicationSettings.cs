#if HISTORIC_RELATIONSHIPS
using Killright.Integration.zKill.History;
using Killright.Storage.GroupHistory.Models;

#endif
namespace Killright.UI.Configuration;

public sealed class ApplicationSettings
{
#if HISTORIC_RELATIONSHIPS
    public GroupHistoryApplicationSettings GroupHistory { get; init; } = new();
#endif
}

#if HISTORIC_RELATIONSHIPS
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
#endif

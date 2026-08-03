namespace Killright.Storage.GroupHistory.Models;

public sealed record GroupHistoryImportBatchOptions(
    int EvidenceInsertBatchSize,
    int ParticipantInsertBatchSize)
{
    public static GroupHistoryImportBatchOptions Default { get; } = new(500, 500);

    public static GroupHistoryImportBatchOptions FromSingleBatchSize(int batchSize)
    {
        if (batchSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(batchSize));

        return new GroupHistoryImportBatchOptions(batchSize, batchSize);
    }
}
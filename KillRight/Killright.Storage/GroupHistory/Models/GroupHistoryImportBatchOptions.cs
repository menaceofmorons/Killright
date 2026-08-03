namespace Killright.Storage.GroupHistory.Models;

public sealed record GroupHistoryImportBatchOptions(
    int EvidenceInsertBatchSize,
    int ParticipantInsertBatchSize)
{
    public const int MinimumBatchSize = 25;
    public const int MaximumBatchSize = 500;
    public const int DefaultBatchSize = 150;

    public static GroupHistoryImportBatchOptions Default { get; } = new(DefaultBatchSize, DefaultBatchSize);

    public static GroupHistoryImportBatchOptions FromSingleBatchSize(int batchSize)
    {
        var clamped = Clamp(batchSize);
        return new GroupHistoryImportBatchOptions(clamped, clamped);
    }

    public static GroupHistoryImportBatchOptions FromConfiguredValues(
        int evidenceInsertBatchSize,
        int participantInsertBatchSize)
    {
        return new GroupHistoryImportBatchOptions(
            Clamp(evidenceInsertBatchSize),
            Clamp(participantInsertBatchSize));
    }

    private static int Clamp(int value)
    {
        if (value < MinimumBatchSize)
            return MinimumBatchSize;

        if (value > MaximumBatchSize)
            return MaximumBatchSize;

        return value;
    }
}
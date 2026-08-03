using Killright.Storage.GroupHistory.Models;

namespace Killright.Storage.GroupHistory;

public interface IGroupHistoryDatabase
{
    string DatabasePath { get; }

    Task<GroupHistoryDatabaseStatus> GetStatusAsync(CancellationToken cancellationToken = default);

    Task EnsureCreatedAsync(CancellationToken cancellationToken = default);

    Task<GroupHistoryUpdateRequirement> GetUpdateRequirementAsync(DateTime utcNow, CancellationToken cancellationToken = default);

    Task MarkImportDayStartedAsync(DateOnly importDateUtc, CancellationToken cancellationToken = default);

    Task MarkImportDayCompletedAsync(
        DateOnly importDateUtc,
        int rawKillmailCount,
        int qualifyingKillmailCount,
        int participantIndexRowCount,
        long pairOccurrenceCount,
        CancellationToken cancellationToken = default);

    Task MarkImportDayFailedAsync(DateOnly importDateUtc, string errorMessage, CancellationToken cancellationToken = default);

    Task ImportEvidenceAndParticipantRowsForDayAsync(
        DateOnly importDateUtc,
        IReadOnlyList<GroupHistoryEvidenceImportRow> evidenceRows,
        IReadOnlyList<GroupHistoryParticipantImportRow> participantRows,
        int rawKillmailCount,
        int qualifyingKillmailCount,
        int qualifyingAttackerCount,
        long candidatePairOccurrenceRows,
        CancellationToken cancellationToken = default);

    Task<GroupHistorySummaryBuildResult> BuildRelationshipSummaryForDayAsync(
        DateOnly importDateUtc,
        CancellationToken cancellationToken = default);
}
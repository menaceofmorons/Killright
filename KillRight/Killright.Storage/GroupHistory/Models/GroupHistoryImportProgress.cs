namespace Killright.Storage.GroupHistory.Models;

public sealed record GroupHistoryImportProgress(
    DateOnly? CurrentDateUtc,
    int TotalDays,
    int CompletedDays,
    int SuccessfulDays,
    int FailedDays,
    string Message);
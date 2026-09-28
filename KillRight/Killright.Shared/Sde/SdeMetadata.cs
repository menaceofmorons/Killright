namespace Killright.Shared.Sde;

public sealed record SdeMetadata(
    long? BuildNumber,
    DateTimeOffset? LastCheckedUtc,
    DateTimeOffset? LastUpdatedUtc,
    DateTimeOffset? LastAttemptUtc,
    string? LastCheckResult);

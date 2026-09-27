namespace Killright.Shared.Sde;

public enum SdeManifestOutcome
{
    Success,
    Failure
}

public sealed record SdeManifestResult(SdeManifestOutcome Outcome, long? BuildNumber);

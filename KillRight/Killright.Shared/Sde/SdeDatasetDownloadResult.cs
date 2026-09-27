namespace Killright.Shared.Sde;

public enum SdeDatasetDownloadOutcome
{
    Success,
    Failure
}

public sealed record SdeDatasetDownloadResult(SdeDatasetDownloadOutcome Outcome);

namespace Killright.Shared.Sde;

public enum SdeCheckOutcome
{
    Skipped,
    UpToDate,
    Replaced,
    ManifestFailure,
    DownloadFailure,
    UnexpectedFailure
}

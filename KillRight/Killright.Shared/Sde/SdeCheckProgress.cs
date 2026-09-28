namespace Killright.Shared.Sde;

public enum SdeCheckStage
{
    CheckingManifest,
    Downloading,
    Importing
}

public sealed record SdeCheckProgress(SdeCheckStage Stage, double? DownloadFraction);

using Killright.Shared.Sde;

namespace Killright.Integration.Sde;

public interface ISdeClient
{
    Task<SdeManifestResult> GetManifestAsync(
        CancellationToken cancellationToken = default);

    Task<SdeDatasetDownloadResult> DownloadDatasetZipAsync(
        string destinationZipPath,
        CancellationToken cancellationToken = default);
}

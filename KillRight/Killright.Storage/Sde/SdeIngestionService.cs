using Killright.Integration.Sde;
using Killright.Shared.Sde;
using Killright.Shared.Time;
using Killright.Storage.Diagnostics;

namespace Killright.Storage.Sde;

public sealed class SdeIngestionService
{
    private readonly ISdeClient _client;
    private readonly ISdeReferenceDataStore _store;
    private readonly int _checkIntervalHours;

    public SdeIngestionService(ISdeClient client, ISdeReferenceDataStore store, int checkIntervalHours)
    {
        _client = client;
        _store = store;
        _checkIntervalHours = checkIntervalHours;
    }

    public async Task RunCheckAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var metadata = await _store.GetMetadataAsync(cancellationToken);
            var now = ApplicationClock.UtcNow;

            if (metadata.LastCheckedUtc is not null
                && now - metadata.LastCheckedUtc.Value < TimeSpan.FromHours(_checkIntervalHours))
                return;

            var manifest = await _client.GetManifestAsync(cancellationToken);

            if (manifest.Outcome != SdeManifestOutcome.Success || manifest.BuildNumber is null)
            {
                await _store.RecordCheckAsync(now, "ManifestFailure", cancellationToken);
                EngineFailureLog.Record("SDE manifest check failed; existing reference tables left untouched.");
                return;
            }

            if (metadata.BuildNumber == manifest.BuildNumber.Value)
            {
                await _store.RecordCheckAsync(now, "UpToDate", cancellationToken);
                return;
            }

            await DownloadAndReplaceAsync(manifest.BuildNumber.Value, now, cancellationToken);
        }
        catch (Exception ex)
        {
            EngineFailureLog.Record($"SDE reference-data check failed; existing tables left untouched. {ex.Message}");
        }
    }

    private async Task DownloadAndReplaceAsync(
        long buildNumber,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var zipPath = Path.Combine(Path.GetTempPath(), $"killright-sde-{Guid.NewGuid():N}.zip");

        try
        {
            var download = await _client.DownloadDatasetZipAsync(zipPath, cancellationToken);

            if (download.Outcome != SdeDatasetDownloadOutcome.Success)
            {
                await _store.RecordCheckAsync(now, "DownloadFailure", cancellationToken);
                EngineFailureLog.Record("SDE dataset download failed; existing reference tables left untouched.");
                return;
            }

            var contents = SdeDatasetParser.ParseZip(zipPath);

            var replacementData = new SdeReplacementData(
                contents.Types,
                contents.SolarSystems,
                contents.NpcCorporationIds,
                buildNumber,
                now);

            await _store.ReplaceTablesAsync(replacementData, cancellationToken);
        }
        finally
        {
            if (File.Exists(zipPath))
                File.Delete(zipPath);
        }
    }
}

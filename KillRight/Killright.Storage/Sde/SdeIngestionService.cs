using Killright.Integration.Sde;
using Killright.Shared.Sde;
using Killright.Shared.Time;
using Killright.Storage.Diagnostics;

namespace Killright.Storage.Sde;

public sealed class SdeIngestionService
{
    private static readonly TimeSpan DefaultInitialRetryDelay = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan DefaultMaximumRetryDelay = TimeSpan.FromMinutes(60);

    private readonly ISdeClient _client;
    private readonly ISdeReferenceDataStore _store;
    private readonly int _checkIntervalHours;
    private readonly TimeSpan _initialRetryDelay;
    private readonly TimeSpan _maximumRetryDelay;

    public SdeIngestionService(
        ISdeClient client,
        ISdeReferenceDataStore store,
        int checkIntervalHours,
        TimeSpan? initialRetryDelay = null,
        TimeSpan? maximumRetryDelay = null)
    {
        _client = client;
        _store = store;
        _checkIntervalHours = checkIntervalHours;
        _initialRetryDelay = initialRetryDelay ?? DefaultInitialRetryDelay;
        _maximumRetryDelay = maximumRetryDelay ?? DefaultMaximumRetryDelay;
    }

    public async Task<SdeCheckOutcome> RunCheckAsync(
        IProgress<SdeCheckProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var metadata = await _store.GetMetadataAsync(cancellationToken);
            var now = ApplicationClock.UtcNow;
            var reloadRequired = metadata.BuildNumber is not null
                && !await _store.HasReferenceDataAsync(cancellationToken);

            if (!reloadRequired
                && metadata.LastCheckedUtc is not null
                && now - metadata.LastCheckedUtc.Value < TimeSpan.FromHours(_checkIntervalHours))
                return SdeCheckOutcome.Skipped;

            progress?.Report(new SdeCheckProgress(SdeCheckStage.CheckingManifest, null));

            var manifest = await _client.GetManifestAsync(cancellationToken);

            if (manifest.Outcome != SdeManifestOutcome.Success || manifest.BuildNumber is null)
            {
                await _store.RecordCheckAsync(now, "ManifestFailure", succeeded: false, cancellationToken);
                EngineFailureLog.Record("SDE manifest check failed; existing reference tables left untouched.");
                return SdeCheckOutcome.ManifestFailure;
            }

            if (!reloadRequired && metadata.BuildNumber == manifest.BuildNumber.Value)
            {
                await _store.RecordCheckAsync(now, "UpToDate", succeeded: true, cancellationToken);
                return SdeCheckOutcome.UpToDate;
            }

            return await DownloadAndReplaceAsync(manifest.BuildNumber.Value, now, progress, cancellationToken);
        }
        catch (Exception ex)
        {
            EngineFailureLog.Record($"SDE reference-data check failed; existing tables left untouched. {ex.Message}");
            return SdeCheckOutcome.UnexpectedFailure;
        }
    }

    public async Task RunCheckWithRetryAsync(CancellationToken cancellationToken = default)
    {
        var delay = _initialRetryDelay;

        while (!cancellationToken.IsCancellationRequested)
        {
            var outcome = await RunCheckAsync(cancellationToken: cancellationToken);

            if (outcome is not (SdeCheckOutcome.ManifestFailure or SdeCheckOutcome.DownloadFailure or SdeCheckOutcome.UnexpectedFailure))
                return;

            try
            {
                await Task.Delay(delay, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            delay = TimeSpan.FromTicks(Math.Min(delay.Ticks * 2, _maximumRetryDelay.Ticks));
        }
    }

    private async Task<SdeCheckOutcome> DownloadAndReplaceAsync(
        long buildNumber,
        DateTimeOffset now,
        IProgress<SdeCheckProgress>? progress,
        CancellationToken cancellationToken)
    {
        var zipPath = Path.Combine(Path.GetTempPath(), $"killright-sde-{Guid.NewGuid():N}.zip");

        try
        {
            progress?.Report(new SdeCheckProgress(SdeCheckStage.Downloading, 0));

            var downloadProgress = progress is null
                ? null
                : new Progress<double>(fraction => progress.Report(new SdeCheckProgress(SdeCheckStage.Downloading, fraction)));

            var download = await _client.DownloadDatasetZipAsync(zipPath, downloadProgress, cancellationToken);

            if (download.Outcome != SdeDatasetDownloadOutcome.Success)
            {
                await _store.RecordCheckAsync(now, "DownloadFailure", succeeded: false, cancellationToken);
                EngineFailureLog.Record("SDE dataset download failed; existing reference tables left untouched.");
                return SdeCheckOutcome.DownloadFailure;
            }

            progress?.Report(new SdeCheckProgress(SdeCheckStage.Importing, null));

            SdeDatasetContents contents;

            try
            {
                contents = SdeDatasetParser.ParseZip(zipPath);
            }
            catch (Exception ex)
            {
                return await RecordIngestFailureAsync("ParseFailure", "SDE dataset parse failed", ex, now, cancellationToken);
            }

            var replacementData = new SdeReplacementData(
                contents.Types,
                contents.SolarSystems,
                contents.NpcCorporationIds,
                buildNumber,
                now,
                contents.Factions);

            try
            {
                await _store.ReplaceTablesAsync(replacementData, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return await RecordIngestFailureAsync("SwapFailure", "SDE table swap failed", ex, now, cancellationToken);
            }

            return SdeCheckOutcome.Replaced;
        }
        finally
        {
            if (File.Exists(zipPath))
                File.Delete(zipPath);
        }
    }

    private async Task<SdeCheckOutcome> RecordIngestFailureAsync(
        string checkResult,
        string description,
        Exception exception,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        EngineFailureLog.Record($"{description}; existing reference tables left untouched. {exception.Message}");
        await _store.RecordCheckAsync(now, checkResult, succeeded: false, cancellationToken);
        return SdeCheckOutcome.UnexpectedFailure;
    }
}

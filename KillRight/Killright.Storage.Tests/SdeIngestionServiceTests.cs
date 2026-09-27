using System.IO.Compression;
using System.Text;
using Killright.Integration.Sde;
using Killright.Shared.Sde;
using Killright.Storage.Database;
using Killright.Storage.Sde;
using Xunit;

namespace Killright.Storage.Tests;

public sealed class SdeIngestionServiceTests
{
    [Fact]
    public async Task RunCheckAsync_FirstRunWithNewBuild_DownloadsAndReplacesTables()
    {
        var (_, store) = CreateStore();
        var client = new FakeSdeClient(manifestBuildNumber: 3542233, zipContentsFactory: BuildValidZip);

        await new SdeIngestionService(client, store, checkIntervalHours: 24).RunCheckAsync();

        Assert.Equal(1, client.ManifestCallCount);
        Assert.Equal(1, client.DownloadCallCount);

        var metadata = await store.GetMetadataAsync();
        Assert.Equal(3542233, metadata.BuildNumber);
        Assert.Equal("Replaced", metadata.LastCheckResult);
        Assert.Equal("Rifter", store.GetTypeName(587));
        Assert.True(store.IsNpcCorporation(1000001));
    }

    [Fact]
    public async Task RunCheckAsync_SameBuildAsStored_TouchesCheckTimeOnly_NoDownload()
    {
        var (_, store) = CreateStore();

        await store.ReplaceTablesAsync(new SdeReplacementData(
            Types: [new SdeType(587, "Rifter")],
            SolarSystems: [],
            NpcCorporationIds: [],
            BuildNumber: 3542233,
            UpdatedUtc: DateTimeOffset.UtcNow.AddDays(-1)));

        var client = new FakeSdeClient(manifestBuildNumber: 3542233, zipContentsFactory: BuildValidZip);

        await new SdeIngestionService(client, store, checkIntervalHours: 24).RunCheckAsync();

        Assert.Equal(1, client.ManifestCallCount);
        Assert.Equal(0, client.DownloadCallCount);

        var metadata = await store.GetMetadataAsync();
        Assert.Equal("UpToDate", metadata.LastCheckResult);
        Assert.Equal("Rifter", store.GetTypeName(587));
    }

    [Fact]
    public async Task RunCheckAsync_CalledAgainWithinCheckInterval_SkipsManifestCallEntirely()
    {
        var (_, store) = CreateStore();
        await store.RecordCheckAsync(DateTimeOffset.UtcNow, "UpToDate");

        var client = new FakeSdeClient(manifestBuildNumber: 3542233, zipContentsFactory: BuildValidZip);

        await new SdeIngestionService(client, store, checkIntervalHours: 24).RunCheckAsync();

        Assert.Equal(0, client.ManifestCallCount);
        Assert.Equal(0, client.DownloadCallCount);
    }

    [Fact]
    public async Task RunCheckAsync_ManifestFailure_RecordsFailureAndLeavesTablesUntouched()
    {
        var (_, store) = CreateStore();
        var client = new FakeSdeClient(manifestBuildNumber: null, zipContentsFactory: BuildValidZip);

        await new SdeIngestionService(client, store, checkIntervalHours: 24).RunCheckAsync();

        Assert.Equal(0, client.DownloadCallCount);

        var metadata = await store.GetMetadataAsync();
        Assert.Equal("ManifestFailure", metadata.LastCheckResult);
        Assert.Null(metadata.BuildNumber);
    }

    [Fact]
    public async Task RunCheckAsync_DownloadFailure_RecordsFailureAndLeavesTablesUntouched()
    {
        var (_, store) = CreateStore();
        var client = new FakeSdeClient(manifestBuildNumber: 3542233, zipContentsFactory: null);

        await new SdeIngestionService(client, store, checkIntervalHours: 24).RunCheckAsync();

        var metadata = await store.GetMetadataAsync();
        Assert.Equal("DownloadFailure", metadata.LastCheckResult);
        Assert.Null(metadata.BuildNumber);
        Assert.Null(store.GetTypeName(587));
    }

    private static byte[] BuildValidZip()
    {
        using var memoryStream = new MemoryStream();

        using (var archive = new ZipArchive(memoryStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteEntry(archive, "types.jsonl", """{"_key": 587, "name": {"en": "Rifter"}, "published": true}""");
            WriteEntry(archive, "mapSolarSystems.jsonl", """{"_key": 30000142, "name": {"en": "Jita"}}""");
            WriteEntry(archive, "npcCorporations.jsonl", """{"_key": 1000001, "deleted": false}""");
        }

        return memoryStream.ToArray();
    }

    private static void WriteEntry(ZipArchive archive, string entryName, string contents)
    {
        var entry = archive.CreateEntry(entryName);
        using var entryStream = entry.Open();
        using var writer = new StreamWriter(entryStream, Encoding.UTF8);
        writer.Write(contents);
    }

    private static (KillRightDatabase Database, DuckDbSdeReferenceDataStore Store) CreateStore()
    {
        var path = Path.Combine(Path.GetTempPath(), $"sdeIngestion.{Guid.NewGuid():N}.duckdb");
        var database = new KillRightDatabase(new KillRightDatabaseOptions { DatabasePath = path });
        database.EnsureCreated();
        return (database, new DuckDbSdeReferenceDataStore(database));
    }

    private sealed class FakeSdeClient : ISdeClient
    {
        private readonly long? _manifestBuildNumber;
        private readonly Func<byte[]>? _zipContentsFactory;

        public FakeSdeClient(long? manifestBuildNumber, Func<byte[]>? zipContentsFactory)
        {
            _manifestBuildNumber = manifestBuildNumber;
            _zipContentsFactory = zipContentsFactory;
        }

        public int ManifestCallCount { get; private set; }

        public int DownloadCallCount { get; private set; }

        public Task<SdeManifestResult> GetManifestAsync(CancellationToken cancellationToken = default)
        {
            ManifestCallCount++;

            return Task.FromResult(_manifestBuildNumber is null
                ? new SdeManifestResult(SdeManifestOutcome.Failure, null)
                : new SdeManifestResult(SdeManifestOutcome.Success, _manifestBuildNumber));
        }

        public Task<SdeDatasetDownloadResult> DownloadDatasetZipAsync(
            string destinationZipPath,
            CancellationToken cancellationToken = default)
        {
            DownloadCallCount++;

            if (_zipContentsFactory is null)
                return Task.FromResult(new SdeDatasetDownloadResult(SdeDatasetDownloadOutcome.Failure));

            File.WriteAllBytes(destinationZipPath, _zipContentsFactory());
            return Task.FromResult(new SdeDatasetDownloadResult(SdeDatasetDownloadOutcome.Success));
        }
    }
}

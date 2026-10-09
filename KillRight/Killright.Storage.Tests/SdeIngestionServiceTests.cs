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

        var outcome = await new SdeIngestionService(client, store, checkIntervalHours: 24).RunCheckAsync();

        Assert.Equal(SdeCheckOutcome.Replaced, outcome);
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
            UpdatedUtc: DateTimeOffset.UtcNow.AddDays(-1),
            Factions: [new SdeFaction(500001, "Caldari State")]));

        var client = new FakeSdeClient(manifestBuildNumber: 3542233, zipContentsFactory: BuildValidZip);

        var outcome = await new SdeIngestionService(client, store, checkIntervalHours: 24).RunCheckAsync();

        Assert.Equal(SdeCheckOutcome.UpToDate, outcome);
        Assert.Equal(1, client.ManifestCallCount);
        Assert.Equal(0, client.DownloadCallCount);

        var metadata = await store.GetMetadataAsync();
        Assert.Equal("UpToDate", metadata.LastCheckResult);
        Assert.Equal("Rifter", store.GetTypeName(587));
    }

    [Fact]
    public async Task RunCheckAsync_FirstRunWithNewBuild_LoadsFactions()
    {
        var (_, store) = CreateStore();
        var client = new FakeSdeClient(manifestBuildNumber: 3542233, zipContentsFactory: BuildValidZip);

        await new SdeIngestionService(client, store, checkIntervalHours: 24).RunCheckAsync();

        Assert.Equal("Caldari State", store.GetFactionName(500001));
    }

    [Fact]
    public async Task RunCheckAsync_ZipWithoutFactionsEntry_FailsIngestionAndLeavesTablesUntouched()
    {
        var (_, store) = CreateStore();

        await store.ReplaceTablesAsync(new SdeReplacementData(
            Types: [new SdeType(587, "Rifter")],
            SolarSystems: [],
            NpcCorporationIds: [],
            BuildNumber: 100,
            UpdatedUtc: DateTimeOffset.UtcNow.AddDays(-2),
            Factions: [new SdeFaction(500001, "Caldari State")]));

        var client = new FakeSdeClient(manifestBuildNumber: 200, zipContentsFactory: BuildZipWithoutFactions);

        var outcome = await new SdeIngestionService(client, store, checkIntervalHours: 24).RunCheckAsync();

        Assert.Equal(SdeCheckOutcome.UnexpectedFailure, outcome);
        Assert.Equal("Rifter", store.GetTypeName(587));
        Assert.Null(store.GetTypeName(999));
        Assert.Equal("Caldari State", store.GetFactionName(500001));
        Assert.Equal(100, (await store.GetMetadataAsync()).BuildNumber);
    }

    [Fact]
    public async Task RunCheckAsync_InstallWithEmptyFactionsAndSameBuild_ReloadsReferenceData()
    {
        var (_, store) = CreateStore();

        await store.ReplaceTablesAsync(new SdeReplacementData(
            Types: [new SdeType(587, "Rifter")],
            SolarSystems: [],
            NpcCorporationIds: [],
            BuildNumber: 3542233,
            UpdatedUtc: DateTimeOffset.UtcNow));
        await store.RecordCheckAsync(DateTimeOffset.UtcNow, "UpToDate", succeeded: true);

        Assert.False(await store.HasReferenceDataAsync());

        var client = new FakeSdeClient(manifestBuildNumber: 3542233, zipContentsFactory: BuildValidZip);

        var outcome = await new SdeIngestionService(client, store, checkIntervalHours: 24).RunCheckAsync();

        Assert.Equal(SdeCheckOutcome.Replaced, outcome);
        Assert.Equal(1, client.DownloadCallCount);
        Assert.Equal("Caldari State", store.GetFactionName(500001));
        Assert.True(await store.HasReferenceDataAsync());
    }

    [Fact]
    public async Task RunCheckAsync_CalledAgainWithinCheckInterval_SkipsManifestCallEntirely()
    {
        var (_, store) = CreateStore();
        await store.RecordCheckAsync(DateTimeOffset.UtcNow, "UpToDate", succeeded: true);

        var client = new FakeSdeClient(manifestBuildNumber: 3542233, zipContentsFactory: BuildValidZip);

        var outcome = await new SdeIngestionService(client, store, checkIntervalHours: 24).RunCheckAsync();

        Assert.Equal(SdeCheckOutcome.Skipped, outcome);
        Assert.Equal(0, client.ManifestCallCount);
        Assert.Equal(0, client.DownloadCallCount);
    }

    [Fact]
    public async Task RunCheckAsync_ManifestFailure_RecordsAttemptOnly_LeavesLastCheckedUtcUntouched()
    {
        var (_, store) = CreateStore();
        var client = new FakeSdeClient(manifestBuildNumber: null, zipContentsFactory: BuildValidZip);

        var outcome = await new SdeIngestionService(client, store, checkIntervalHours: 24).RunCheckAsync();

        Assert.Equal(SdeCheckOutcome.ManifestFailure, outcome);
        Assert.Equal(0, client.DownloadCallCount);

        var metadata = await store.GetMetadataAsync();
        Assert.Equal("ManifestFailure", metadata.LastCheckResult);
        Assert.Null(metadata.BuildNumber);
        Assert.Null(metadata.LastCheckedUtc);
        Assert.NotNull(metadata.LastAttemptUtc);
    }

    [Fact]
    public async Task RunCheckAsync_DownloadFailure_RecordsAttemptOnly_LeavesLastCheckedUtcUntouched()
    {
        var (_, store) = CreateStore();
        var client = new FakeSdeClient(manifestBuildNumber: 3542233, zipContentsFactory: null);

        var outcome = await new SdeIngestionService(client, store, checkIntervalHours: 24).RunCheckAsync();

        Assert.Equal(SdeCheckOutcome.DownloadFailure, outcome);

        var metadata = await store.GetMetadataAsync();
        Assert.Equal("DownloadFailure", metadata.LastCheckResult);
        Assert.Null(metadata.BuildNumber);
        Assert.Null(metadata.LastCheckedUtc);
        Assert.NotNull(metadata.LastAttemptUtc);
        Assert.Null(store.GetTypeName(587));
    }

    [Fact]
    public async Task RunCheckWithRetryAsync_ManifestFailsTwiceThenSucceeds_RetriesUntilSuccess()
    {
        var (_, store) = CreateStore();
        var client = new FakeSdeClient(new Queue<long?>(new long?[] { null, null, 3542233 }), BuildValidZip);
        var service = new SdeIngestionService(
            client,
            store,
            checkIntervalHours: 24,
            initialRetryDelay: TimeSpan.FromMilliseconds(5),
            maximumRetryDelay: TimeSpan.FromMilliseconds(20));

        await service.RunCheckWithRetryAsync();

        Assert.Equal(3, client.ManifestCallCount);

        var metadata = await store.GetMetadataAsync();
        Assert.Equal(3542233, metadata.BuildNumber);
        Assert.Equal("Replaced", metadata.LastCheckResult);
    }

    [Fact]
    public async Task RunCheckWithRetryAsync_PersistentManifestFailure_StopsWhenCancelled()
    {
        var (_, store) = CreateStore();
        var client = new FakeSdeClient(manifestBuildNumber: null, zipContentsFactory: BuildValidZip);
        var service = new SdeIngestionService(
            client,
            store,
            checkIntervalHours: 24,
            initialRetryDelay: TimeSpan.FromMilliseconds(5),
            maximumRetryDelay: TimeSpan.FromMilliseconds(20));

        using var cancellationTokenSource = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));

        await service.RunCheckWithRetryAsync(cancellationTokenSource.Token);

        Assert.True(client.ManifestCallCount > 1);

        var metadata = await store.GetMetadataAsync();
        Assert.Equal("ManifestFailure", metadata.LastCheckResult);
        Assert.Null(metadata.LastCheckedUtc);
        Assert.NotNull(metadata.LastAttemptUtc);
    }

    private static byte[] BuildValidZip()
    {
        using var memoryStream = new MemoryStream();

        using (var archive = new ZipArchive(memoryStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteEntry(archive, "types.jsonl", """{"_key": 587, "name": {"en": "Rifter"}, "published": true}""");
            WriteEntry(archive, "mapSolarSystems.jsonl", """{"_key": 30000142, "name": {"en": "Jita"}}""");
            WriteEntry(archive, "npcCorporations.jsonl", """{"_key": 1000001, "deleted": false}""");
            WriteEntry(archive, "factions.jsonl", """{"_key": 500001, "name": {"en": "Caldari State"}}""");
        }

        return memoryStream.ToArray();
    }

    private static byte[] BuildZipWithoutFactions()
    {
        using var memoryStream = new MemoryStream();

        using (var archive = new ZipArchive(memoryStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteEntry(archive, "types.jsonl", """{"_key": 999, "name": {"en": "Replaced Type"}, "published": true}""");
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

    private static (KillRightDatabase Database, SdeReferenceDataStore Store) CreateStore()
    {
        var path = Path.Combine(Path.GetTempPath(), $"sdeIngestion.{Guid.NewGuid():N}.db");
        var database = new KillRightDatabase(new KillRightDatabaseOptions { DatabasePath = path });
        database.EnsureCreated();
        return (database, new SdeReferenceDataStore(database));
    }

    private sealed class FakeSdeClient : ISdeClient
    {
        private readonly Queue<long?> _manifestBuildNumbers;
        private readonly Func<byte[]>? _zipContentsFactory;

        public FakeSdeClient(long? manifestBuildNumber, Func<byte[]>? zipContentsFactory)
            : this(new Queue<long?>(new[] { manifestBuildNumber }), zipContentsFactory)
        {
        }

        public FakeSdeClient(Queue<long?> manifestBuildNumbers, Func<byte[]>? zipContentsFactory)
        {
            _manifestBuildNumbers = manifestBuildNumbers;
            _zipContentsFactory = zipContentsFactory;
        }

        public int ManifestCallCount { get; private set; }

        public int DownloadCallCount { get; private set; }

        public Task<SdeManifestResult> GetManifestAsync(CancellationToken cancellationToken = default)
        {
            ManifestCallCount++;

            var buildNumber = _manifestBuildNumbers.Count > 1
                ? _manifestBuildNumbers.Dequeue()
                : _manifestBuildNumbers.Peek();

            return Task.FromResult(buildNumber is null
                ? new SdeManifestResult(SdeManifestOutcome.Failure, null)
                : new SdeManifestResult(SdeManifestOutcome.Success, buildNumber));
        }

        public Task<SdeDatasetDownloadResult> DownloadDatasetZipAsync(
            string destinationZipPath,
            IProgress<double>? progress = null,
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

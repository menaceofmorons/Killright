using Killright.Integration.zKill;
using Killright.Shared.zKill;
using Killright.Storage.Killmails;
using Killright.UI.Scan;
using Xunit;

namespace Killright.UI.Tests.Scan;

public sealed class KillmailPurgeSchedulerTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 29, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task StartupPass_RunsOnce()
    {
        var cache = new FakeCache();
        var scheduler = new KillmailPurgeScheduler(cache);

        var ran = await scheduler.RunStartupPassAsync();

        Assert.True(ran);
        Assert.Equal(1, cache.Calls);
    }

    [Fact]
    public async Task PostScanPass_WithinFourHoursOfLastPass_DoesNotRun()
    {
        var clock = new FakeClock(Start);
        var cache = new FakeCache();
        var scheduler = new KillmailPurgeScheduler(cache, clock.UtcNow);

        await scheduler.RunStartupPassAsync();
        clock.Now = Start.AddHours(4);

        var ran = await scheduler.RunPostScanPassAsync();

        Assert.False(ran);
        Assert.Equal(1, cache.Calls);
    }

    [Fact]
    public async Task PostScanPass_OverFourHoursSinceLastPass_Runs()
    {
        var clock = new FakeClock(Start);
        var cache = new FakeCache();
        var scheduler = new KillmailPurgeScheduler(cache, clock.UtcNow);

        await scheduler.RunStartupPassAsync();
        clock.Now = Start.AddHours(4).AddMinutes(1);

        var ran = await scheduler.RunPostScanPassAsync();

        Assert.True(ran);
        Assert.Equal(2, cache.Calls);
    }

    [Fact]
    public async Task PostScanPass_NoPriorPass_Runs()
    {
        var cache = new FakeCache();
        var scheduler = new KillmailPurgeScheduler(cache);

        var ran = await scheduler.RunPostScanPassAsync();

        Assert.True(ran);
        Assert.Equal(1, cache.Calls);
    }

    [Fact]
    public async Task PostScanPass_AfterSecondPass_RestartsInterval()
    {
        var clock = new FakeClock(Start);
        var cache = new FakeCache();
        var scheduler = new KillmailPurgeScheduler(cache, clock.UtcNow);

        await scheduler.RunStartupPassAsync();
        clock.Now = Start.AddHours(5);
        await scheduler.RunPostScanPassAsync();
        clock.Now = Start.AddHours(6);

        var ran = await scheduler.RunPostScanPassAsync();

        Assert.False(ran);
        Assert.Equal(2, cache.Calls);
    }

    [Fact]
    public async Task Passes_WhileScanRunning_DoNotRun()
    {
        var cache = new FakeCache();
        var scheduler = new KillmailPurgeScheduler(cache);

        scheduler.ScanStarted();

        Assert.False(await scheduler.RunStartupPassAsync());
        Assert.False(await scheduler.RunPostScanPassAsync());
        Assert.Equal(0, cache.Calls);

        scheduler.ScanFinished();

        Assert.True(await scheduler.RunPostScanPassAsync());
        Assert.Equal(1, cache.Calls);
    }

    [Fact]
    public async Task Passes_WithOverlappingScans_WaitForAllScansToFinish()
    {
        var cache = new FakeCache();
        var scheduler = new KillmailPurgeScheduler(cache);

        scheduler.ScanStarted();
        scheduler.ScanStarted();
        scheduler.ScanFinished();

        Assert.False(await scheduler.RunPostScanPassAsync());

        scheduler.ScanFinished();

        Assert.True(await scheduler.RunPostScanPassAsync());
    }

    [Fact]
    public async Task TwoTriggers_NeverOverlap()
    {
        var release = new TaskCompletionSource();
        var cache = new FakeCache(release.Task);
        var scheduler = new KillmailPurgeScheduler(cache);

        var first = scheduler.RunStartupPassAsync();
        await cache.Entered.Task;

        var second = await scheduler.RunPostScanPassAsync();

        Assert.False(second);

        release.SetResult();

        Assert.True(await first);
        Assert.Equal(1, cache.Calls);
        Assert.Equal(1, cache.MaxConcurrent);
    }

    [Fact]
    public async Task FailedPass_IsLoggedAndDoesNotStopNextTrigger()
    {
        var cache = new FakeCache { FailNext = true };
        var logged = new List<string>();
        var scheduler = new KillmailPurgeScheduler(cache, logFailure: logged.Add);

        var first = await scheduler.RunStartupPassAsync();
        var second = await scheduler.RunPostScanPassAsync();

        Assert.False(first);
        Assert.Single(logged);
        Assert.True(second);
        Assert.Equal(2, cache.Calls);
    }

    [Fact]
    public async Task SuccessfulPass_RecordsTagAndDuration()
    {
        var recorded = new List<(string Tag, double Milliseconds)>();
        var scheduler = new KillmailPurgeScheduler(new FakeCache(), recordPass: (tag, ms) => recorded.Add((tag, ms)));

        await scheduler.RunStartupPassAsync();

        var entry = Assert.Single(recorded);
        Assert.Equal(KillmailPurgeScheduler.StartupTag, entry.Tag);
        Assert.True(entry.Milliseconds >= 0);
    }

    [Fact]
    public async Task RecordPassFailure_IsSwallowed()
    {
        var scheduler = new KillmailPurgeScheduler(
            new FakeCache(),
            recordPass: (_, _) => throw new InvalidOperationException("timing file locked"));

        Assert.True(await scheduler.RunStartupPassAsync());
    }

    [Fact]
    public void ScanPath_DoesNotCallRemoveExpiredAsync()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "KillRight.sln")))
            directory = directory.Parent;

        Assert.NotNull(directory);

        var mainWindow = File.ReadAllText(Path.Combine(directory!.FullName, "Killright.UI", "MainWindow.xaml.cs"));
        var coordinator = File.ReadAllText(Path.Combine(directory.FullName, "Killright.UI", "Scan", "ScanCoordinator.cs"));

        Assert.DoesNotContain("RemoveExpiredAsync", mainWindow);
        Assert.DoesNotContain("RemoveExpiredAsync", coordinator);
    }

    private sealed class FakeClock
    {
        public FakeClock(DateTimeOffset now) => Now = now;

        public DateTimeOffset Now { get; set; }

        public DateTimeOffset UtcNow() => Now;
    }

    private sealed class FakeCache : IRecentKillmailCache
    {
        private readonly Task? _gate;
        private int _concurrent;

        public FakeCache(Task? gate = null) => _gate = gate;

        public int Calls { get; private set; }
        public int MaxConcurrent { get; private set; }
        public bool FailNext { get; set; }
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task RemoveExpiredAsync(CancellationToken cancellationToken = default)
        {
            var concurrent = Interlocked.Increment(ref _concurrent);
            MaxConcurrent = Math.Max(MaxConcurrent, concurrent);
            Calls++;
            Entered.TrySetResult();

            try
            {
                if (_gate is not null)
                    await _gate;

                if (FailNext)
                {
                    FailNext = false;
                    throw new InvalidOperationException("purge failed");
                }
            }
            finally
            {
                Interlocked.Decrement(ref _concurrent);
            }
        }

        public Task<zKillActivity> GetDerivedActivityAsync(long characterId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<PilotRecentKillmail?> GetMostRecentKillmailAsync(long characterId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }
}

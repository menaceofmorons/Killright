using Killright.UI.Scan;
using Xunit;

namespace Killright.UI.Tests.Scan;

public sealed class ScanCoordinatorTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task Submit_RunsScanOffTheCallingThread()
    {
        var callerThread = Environment.CurrentManagedThreadId;
        var scanThread = 0;
        var isPoolThread = false;
        var hasSynchronizationContext = true;

        var harness = new Harness((_, _, _) =>
        {
            scanThread = Environment.CurrentManagedThreadId;
            isPoolThread = Thread.CurrentThread.IsThreadPoolThread;
            hasSynchronizationContext = SynchronizationContext.Current is not null;
            return Task.CompletedTask;
        });

        await harness.Coordinator.Submit(["Lukas Naarii"]).WaitAsync(Timeout);

        Assert.NotEqual(callerThread, scanThread);
        Assert.True(isPoolThread);
        Assert.False(hasSynchronizationContext);
    }

    [Fact]
    public async Task Submit_SecondListCancelsFirst_OnlySecondIsCurrentAndApplied()
    {
        var applied = new List<string>();
        var firstStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var harness = new Harness(async (names, _, context) =>
        {
            if (names[0] == "Lukas Naarii")
            {
                firstStarted.SetResult();
                await release.Task;
            }

            if (context.IsCurrent())
                lock (applied)
                    applied.Add(names[0]);
        });

        var first = harness.Coordinator.Submit(["Lukas Naarii"]);
        await firstStarted.Task.WaitAsync(Timeout);
        var second = harness.Coordinator.Submit(["T'ral Vsengne"]);
        release.SetResult();

        await Task.WhenAll(first, second).WaitAsync(Timeout);

        Assert.Equal(["T'ral Vsengne"], applied);
    }

    [Fact]
    public async Task Submit_SecondListCancelsFirst_FirstTokenIsCancelled()
    {
        var firstStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstCancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var harness = new Harness(async (names, _, context) =>
        {
            if (names[0] != "Lukas Naarii")
                return;

            context.Token.Register(() => firstCancelled.TrySetResult());
            firstStarted.SetResult();
            await Task.Delay(Timeout, context.Token);
        });

        var first = harness.Coordinator.Submit(["Lukas Naarii"]);
        await firstStarted.Task.WaitAsync(Timeout);
        var second = harness.Coordinator.Submit(["T'ral Vsengne"]);

        await firstCancelled.Task.WaitAsync(Timeout);
        await Task.WhenAll(first, second).WaitAsync(Timeout);

        Assert.Empty(harness.Faults);
    }

    [Fact]
    public async Task Submit_NewScanWaitsForCancelledScanToFinish_NeverOverlaps()
    {
        var firstStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var running = 0;
        var maxRunning = 0;

        var harness = new Harness(async (names, _, _) =>
        {
            var now = Interlocked.Increment(ref running);
            InterlockedMax(ref maxRunning, now);

            try
            {
                if (names[0] == "Lukas Naarii")
                {
                    firstStarted.SetResult();
                    await release.Task;
                }
                else
                {
                    secondStarted.SetResult();
                }
            }
            finally
            {
                Interlocked.Decrement(ref running);
            }
        });

        var first = harness.Coordinator.Submit(["Lukas Naarii"]);
        await firstStarted.Task.WaitAsync(Timeout);
        var second = harness.Coordinator.Submit(["T'ral Vsengne"]);

        await Task.Delay(100);
        Assert.False(secondStarted.Task.IsCompleted);

        release.SetResult();
        await Task.WhenAll(first, second).WaitAsync(Timeout);

        Assert.True(secondStarted.Task.IsCompletedSuccessfully);
        Assert.Equal(1, maxRunning);
    }

    [Fact]
    public async Task Submit_ThreeRapidLists_MiddleScanNeverRuns()
    {
        var firstStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var ran = new List<string>();

        var harness = new Harness(async (names, _, _) =>
        {
            lock (ran)
                ran.Add(names[0]);

            if (names[0] == "Lukas Naarii")
            {
                firstStarted.SetResult();
                await release.Task;
            }
        });

        var first = harness.Coordinator.Submit(["Lukas Naarii"]);
        await firstStarted.Task.WaitAsync(Timeout);
        var second = harness.Coordinator.Submit(["T'ral Vsengne"]);
        var third = harness.Coordinator.Submit(["syMptom NZ"]);
        release.SetResult();

        await Task.WhenAll(first, second, third).WaitAsync(Timeout);

        Assert.Equal(["Lukas Naarii", "syMptom NZ"], ran);
    }

    [Fact]
    public async Task Submit_ScanStartedBeforeAnyScanFinished_AndBalancedForCompletedCancelledAndFaultedScans()
    {
        var firstStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var harness = new Harness(async (names, _, _) =>
        {
            if (names[0] == "Lukas Naarii")
            {
                firstStarted.SetResult();
                await release.Task;
            }

            if (names[0] == "syMptom NZ")
                throw new InvalidOperationException("scan fault");
        });

        var first = harness.Coordinator.Submit(["Lukas Naarii"]);
        await firstStarted.Task.WaitAsync(Timeout);
        var second = harness.Coordinator.Submit(["T'ral Vsengne"]);
        var third = harness.Coordinator.Submit(["syMptom NZ"]);

        Assert.Equal(3, harness.Started);
        Assert.Equal(0, harness.Finished);

        release.SetResult();
        await Task.WhenAll(first, second, third).WaitAsync(Timeout);

        Assert.Equal(3, harness.Started);
        Assert.Equal(3, harness.Finished);
    }

    [Fact]
    public async Task Submit_FaultedScan_IsLoggedNotThrownAndNextScanStillRuns()
    {
        var ran = new List<string>();

        var harness = new Harness((names, _, _) =>
        {
            if (names[0] == "Lukas Naarii")
                throw new InvalidOperationException("scan fault");

            lock (ran)
                ran.Add(names[0]);

            return Task.CompletedTask;
        });

        await harness.Coordinator.Submit(["Lukas Naarii"]).WaitAsync(Timeout);
        await harness.Coordinator.Submit(["T'ral Vsengne"]).WaitAsync(Timeout);

        Assert.Single(harness.Faults);
        Assert.Equal("scan fault", harness.Faults[0].Message);
        Assert.Equal(["T'ral Vsengne"], ran);
    }

    [Fact]
    public async Task Submit_ScanCancelledDuringItsStage_IsNotLoggedAndStillFinishes()
    {
        var stageReached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var harness = new Harness(async (_, _, context) =>
        {
            stageReached.SetResult();
            await Task.Delay(Timeout, context.Token);
        });

        var first = harness.Coordinator.Submit(["Lukas Naarii"]);
        await stageReached.Task.WaitAsync(Timeout);
        harness.Coordinator.CancelCurrent();

        await first.WaitAsync(Timeout);

        Assert.Empty(harness.Faults);
        Assert.Equal(1, harness.Finished);
    }

    [Fact]
    public async Task IsCurrent_AfterScanFinishes_IsFalse()
    {
        ScanContext? captured = null;

        var harness = new Harness((_, _, context) =>
        {
            captured = context;
            Assert.True(context.IsCurrent());
            return Task.CompletedTask;
        });

        await harness.Coordinator.Submit(["Lukas Naarii"]).WaitAsync(Timeout);

        Assert.NotNull(captured);
        Assert.False(captured!.IsCurrent());
    }

    [Fact]
    public async Task Submit_PassesPilotListAndTimingsToScan()
    {
        IReadOnlyList<string>? receivedNames = null;
        Killright.Storage.Diagnostics.ScanTimings? receivedTimings = null;
        var timings = new Killright.Storage.Diagnostics.ScanTimings();

        var harness = new Harness((names, scanTimings, _) =>
        {
            receivedNames = names;
            receivedTimings = scanTimings;
            return Task.CompletedTask;
        });

        await harness.Coordinator.Submit(["Lukas Naarii", "T'ral Vsengne"], timings).WaitAsync(Timeout);

        Assert.Equal(["Lukas Naarii", "T'ral Vsengne"], receivedNames);
        Assert.Same(timings, receivedTimings);
    }

    private static void InterlockedMax(ref int target, int value)
    {
        int current;

        do
        {
            current = Volatile.Read(ref target);

            if (value <= current)
                return;
        }
        while (Interlocked.CompareExchange(ref target, value, current) != current);
    }

    private sealed class Harness
    {
        private int _started;
        private int _finished;

        public Harness(Func<IReadOnlyList<string>, Killright.Storage.Diagnostics.ScanTimings?, ScanContext, Task> runScan)
        {
            Coordinator = new ScanCoordinator(
                runScan,
                () => Interlocked.Increment(ref _started),
                () => Interlocked.Increment(ref _finished),
                exception =>
                {
                    lock (Faults)
                        Faults.Add(exception);
                });
        }

        public ScanCoordinator Coordinator { get; }

        public List<Exception> Faults { get; } = [];

        public int Started => Volatile.Read(ref _started);

        public int Finished => Volatile.Read(ref _finished);
    }
}

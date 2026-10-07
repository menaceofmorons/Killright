using Killright.UI.Scan;
using Xunit;

namespace Killright.UI.Tests.Scan;

public sealed class IdleCheckpointSchedulerTests
{
    [Fact]
    public async Task ActivityFinished_CheckpointsAfterTheDelayWhenIdleAndTheWalIsNotEmpty()
    {
        var harness = new Harness { WalHasData = true };

        var ran = await harness.Scheduler.ActivityFinished();

        Assert.True(ran);
        Assert.Equal(1, harness.Checkpoints);
        Assert.Equal([TimeSpan.FromSeconds(30)], harness.Delays);
    }

    [Fact]
    public async Task ActivityFinished_NoCheckpointWhenTheWalIsEmpty()
    {
        var harness = new Harness { WalHasData = false };

        var ran = await harness.Scheduler.ActivityFinished();

        Assert.False(ran);
        Assert.Equal(0, harness.Checkpoints);
    }

    [Fact]
    public async Task ActivityStarted_WithinTheDelay_PreventsTheCheckpoint()
    {
        var harness = new Harness { WalHasData = true, HoldDelay = true };

        harness.Scheduler.ActivityStarted();
        var pending = harness.Scheduler.ActivityFinished();
        harness.Scheduler.ActivityStarted();
        harness.ReleaseDelay();

        Assert.False(await pending);
        Assert.Equal(0, harness.Checkpoints);
    }

    [Fact]
    public async Task ActivityStartedAndFinishedAgain_OnlyTheLatestFinishCheckpoints()
    {
        var harness = new Harness { WalHasData = true, HoldDelay = true };

        harness.Scheduler.ActivityStarted();
        var first = harness.Scheduler.ActivityFinished();
        harness.Scheduler.ActivityStarted();
        var second = harness.Scheduler.ActivityFinished();
        harness.ReleaseDelay();

        Assert.False(await first);
        Assert.True(await second);
        Assert.Equal(1, harness.Checkpoints);
    }

    [Fact]
    public async Task ActivityFinished_WhileAnotherActivityIsStillRunning_NoCheckpoint()
    {
        var harness = new Harness { WalHasData = true };

        harness.Scheduler.ActivityStarted();
        harness.Scheduler.ActivityStarted();

        Assert.False(await harness.Scheduler.ActivityFinished());
        Assert.Equal(0, harness.Checkpoints);

        Assert.True(await harness.Scheduler.ActivityFinished());
        Assert.Equal(1, harness.Checkpoints);
    }

    [Fact]
    public async Task Checkpoint_RecordsTheIdleTagAndDuration()
    {
        var harness = new Harness { WalHasData = true };

        await harness.Scheduler.ActivityFinished();

        var entry = Assert.Single(harness.Recorded);
        Assert.Equal(IdleCheckpointScheduler.IdleTag, entry.Tag);
        Assert.True(entry.Milliseconds >= 0);
    }

    [Fact]
    public async Task CheckpointFailure_IsLoggedAndTheNextFinishStillCheckpoints()
    {
        var harness = new Harness { WalHasData = true, FailNext = true };

        Assert.False(await harness.Scheduler.ActivityFinished());
        Assert.Single(harness.Logged);

        Assert.True(await harness.Scheduler.ActivityFinished());
        Assert.Equal(2, harness.Checkpoints);
    }

    [Fact]
    public async Task RecordFailure_IsSwallowed()
    {
        var scheduler = new IdleCheckpointScheduler(
            TimeSpan.Zero,
            () => true,
            () => { },
            _ => Task.CompletedTask,
            recordCheckpoint: (_, _) => throw new InvalidOperationException("timing file locked"));

        Assert.True(await scheduler.ActivityFinished());
    }

    private sealed class Harness
    {
        private readonly TaskCompletionSource _delayGate = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Harness()
        {
            Scheduler = new IdleCheckpointScheduler(
                TimeSpan.FromSeconds(30),
                () => WalHasData,
                () =>
                {
                    Checkpoints++;

                    if (FailNext)
                    {
                        FailNext = false;
                        throw new InvalidOperationException("checkpoint failed");
                    }
                },
                delay =>
                {
                    lock (Delays)
                        Delays.Add(delay);

                    return HoldDelay ? _delayGate.Task : Task.CompletedTask;
                },
                Logged.Add,
                (tag, milliseconds) => Recorded.Add((tag, milliseconds)));
        }

        public IdleCheckpointScheduler Scheduler { get; }

        public bool WalHasData { get; set; }

        public bool HoldDelay { get; set; }

        public bool FailNext { get; set; }

        public int Checkpoints { get; private set; }

        public List<TimeSpan> Delays { get; } = [];

        public List<string> Logged { get; } = [];

        public List<(string Tag, double Milliseconds)> Recorded { get; } = [];

        public void ReleaseDelay() => _delayGate.SetResult();
    }
}

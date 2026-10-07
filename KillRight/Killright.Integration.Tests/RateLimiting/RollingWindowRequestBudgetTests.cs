using Killright.Integration.RateLimiting;
using Xunit;

namespace Killright.Integration.Tests.RateLimiting;

public sealed class RollingWindowRequestBudgetTests
{
    private static readonly TimeSpan Window = TimeSpan.FromSeconds(60);

    [Fact]
    public async Task WaitAsync_StartsWithinBudget_PassWithNoWait()
    {
        var clock = new FakeClock();
        var budget = Create(5, clock);

        for (var call = 0; call < 5; call++)
            await budget.WaitAsync();

        Assert.Empty(clock.Delays);
        Assert.Equal(0, budget.TotalWaitMilliseconds);
    }

    [Fact]
    public async Task WaitAsync_StartBeyondBudget_WaitsUntilFirstStartLeavesWindow()
    {
        var clock = new FakeClock();
        var budget = Create(3, clock);

        await budget.WaitAsync();
        clock.Advance(TimeSpan.FromSeconds(10));
        await budget.WaitAsync();
        await budget.WaitAsync();
        await budget.WaitAsync();

        Assert.Equal([TimeSpan.FromSeconds(50)], clock.Delays);
        Assert.Equal(TimeSpan.FromSeconds(60), clock.Elapsed);
        Assert.Equal(50_000, budget.TotalWaitMilliseconds);
    }

    [Fact]
    public async Task WaitAsync_LongRandomSchedule_NoRollingWindowHoldsMoreThanBudget()
    {
        const int requestBudget = 20;
        var random = new Random(25);
        var clock = new FakeClock();
        var budget = Create(requestBudget, clock);
        var starts = new List<long>();

        for (var call = 0; call < 2000; call++)
        {
            clock.Advance(TimeSpan.FromMilliseconds(random.Next(0, 900)));
            await budget.WaitAsync();
            starts.Add(clock.Now);
        }

        for (var index = 0; index < starts.Count; index++)
        {
            var windowEnd = starts[index] + Window.Ticks;
            var inWindow = starts.Skip(index).TakeWhile(start => start < windowEnd).Count();

            Assert.True(inWindow <= requestBudget, $"{inWindow} starts in the window beginning at {starts[index]}");
        }
    }

    [Fact]
    public async Task WaitAsync_CancelledWhileWaiting_ThrowsOperationCanceled()
    {
        var budget = new RollingWindowRequestBudget(1, Window, () => 0, (_, token) => Task.Delay(Timeout.Infinite, token));
        using var cancellation = new CancellationTokenSource();

        await budget.WaitAsync();
        var waiter = budget.WaitAsync(cancellation.Token);

        Assert.False(waiter.IsCompleted);

        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiter);
    }

    [Fact]
    public async Task WaitAsync_AlreadyCancelledToken_ThrowsWithoutRecordingStart()
    {
        var clock = new FakeClock();
        var budget = Create(1, clock);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => budget.WaitAsync(cancellation.Token));
        await budget.WaitAsync();

        Assert.Empty(clock.Delays);
    }

    [Fact]
    public async Task Pause_WaitAsyncThrowsWithoutRecordingStartUntilPauseEnds()
    {
        var clock = new FakeClock();
        var budget = Create(1, clock);

        budget.Pause(TimeSpan.FromSeconds(30));

        Assert.True(budget.IsPaused);
        await Assert.ThrowsAsync<ZkillRateLimitedException>(() => budget.WaitAsync());

        clock.Advance(TimeSpan.FromSeconds(30));

        Assert.False(budget.IsPaused);
        await budget.WaitAsync();

        Assert.Empty(clock.Delays);
    }

    [Fact]
    public async Task Pause_WhileCallerWaitsForBudget_CallerThrowsWhenItWakes()
    {
        var clock = new FakeClock();
        RollingWindowRequestBudget? budget = null;
        budget = new RollingWindowRequestBudget(1, Window, () => clock.Now, (span, _) =>
        {
            budget!.Pause(TimeSpan.FromSeconds(120));
            clock.Advance(span);
            return Task.CompletedTask;
        });

        await budget.WaitAsync();

        await Assert.ThrowsAsync<ZkillRateLimitedException>(() => budget.WaitAsync());
    }

    [Fact]
    public void Pause_NonPositiveDuration_DoesNotPause()
    {
        var budget = Create(1, new FakeClock());

        budget.Pause(TimeSpan.Zero);
        budget.Pause(TimeSpan.FromSeconds(-5));

        Assert.False(budget.IsPaused);
    }

    [Fact]
    public async Task Pause_ShorterPauseAfterLongerOne_KeepsTheLongerPause()
    {
        var clock = new FakeClock();
        var budget = Create(1, clock);

        budget.Pause(TimeSpan.FromSeconds(60));
        budget.Pause(TimeSpan.FromSeconds(10));
        clock.Advance(TimeSpan.FromSeconds(30));

        Assert.True(budget.IsPaused);
        await Assert.ThrowsAsync<ZkillRateLimitedException>(() => budget.WaitAsync());
    }

    [Fact]
    public async Task TotalWaitMilliseconds_AccumulatesEveryWait()
    {
        var clock = new FakeClock();
        var budget = Create(1, clock);

        await budget.WaitAsync();
        await budget.WaitAsync();
        await budget.WaitAsync();

        Assert.Equal(120_000, budget.TotalWaitMilliseconds);
    }

    [Theory]
    [InlineData(0, 60)]
    [InlineData(-1, 60)]
    [InlineData(5, 0)]
    public void Constructor_NonPositiveArguments_Throws(int requestBudget, int windowSeconds)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new RollingWindowRequestBudget(requestBudget, TimeSpan.FromSeconds(windowSeconds)));
    }

    private static RollingWindowRequestBudget Create(int requestBudget, FakeClock clock)
    {
        return new RollingWindowRequestBudget(requestBudget, Window, () => clock.Now, clock.DelayAsync);
    }

    private sealed class FakeClock
    {
        public long Now { get; private set; }

        public List<TimeSpan> Delays { get; } = [];

        public TimeSpan Elapsed => TimeSpan.FromTicks(Now);

        public void Advance(TimeSpan span) => Now += span.Ticks;

        public Task DelayAsync(TimeSpan span, CancellationToken cancellationToken)
        {
            Delays.Add(span);
            Advance(span);
            return Task.CompletedTask;
        }
    }
}

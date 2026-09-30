using System.Diagnostics;
using Killright.Integration.RateLimiting;
using Xunit;

namespace Killright.Integration.Tests.RateLimiting;

public sealed class RequestStartLimiterTests
{
    [Fact]
    public async Task WaitAsync_ManyCallsAtFixedClock_NoOneSecondWindowExceedsRate()
    {
        const int rate = 15;
        var waits = new List<TimeSpan>();
        var limiter = new RequestStartLimiter(rate, () => 0, (span, _) =>
        {
            waits.Add(span);
            return Task.CompletedTask;
        });

        for (var call = 0; call < 60; call++)
            await limiter.WaitAsync();

        var starts = new[] { TimeSpan.Zero }.Concat(waits).OrderBy(start => start).ToList();

        Assert.Equal(60, starts.Count);

        foreach (var start in starts)
        {
            var inWindow = starts.Count(other => other >= start && other < start + TimeSpan.FromSeconds(1));
            Assert.True(inWindow <= rate, $"{inWindow} starts in one second from {start}");
        }
    }

    [Fact]
    public void WaitAsync_CallsIssuedInOrder_ReceiveIncreasingSlots()
    {
        var waits = new List<TimeSpan>();
        var limiter = new RequestStartLimiter(10, () => 0, (span, _) =>
        {
            waits.Add(span);
            return Task.CompletedTask;
        });

        for (var call = 0; call < 10; call++)
            _ = limiter.WaitAsync();

        Assert.Equal(9, waits.Count);
        Assert.Equal(waits.OrderBy(wait => wait).ToList(), waits);
        Assert.Equal(waits.Distinct().Count(), waits.Count);
    }

    [Fact]
    public async Task WaitAsync_EightConcurrentCallers_RealClockHonoursRate()
    {
        var limiter = new RequestStartLimiter(200);
        var stopwatch = Stopwatch.StartNew();

        var callers = Enumerable.Range(0, 8).Select(async _ =>
        {
            for (var call = 0; call < 5; call++)
                await limiter.WaitAsync();
        });

        await Task.WhenAll(callers);
        stopwatch.Stop();

        Assert.True(stopwatch.ElapsedMilliseconds >= 150, $"elapsed {stopwatch.ElapsedMilliseconds} ms");
    }

    [Fact]
    public async Task WaitAsync_CancelledWhileWaiting_ThrowsOperationCanceled()
    {
        var limiter = new RequestStartLimiter(1000, () => 0, (_, token) => Task.Delay(Timeout.Infinite, token));
        using var cancellation = new CancellationTokenSource();

        await limiter.WaitAsync();
        var waiter = limiter.WaitAsync(cancellation.Token);

        Assert.False(waiter.IsCompleted);

        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiter);
    }

    [Fact]
    public async Task WaitAsync_AlreadyCancelledToken_ReturnsCancelledWithoutConsumingSlot()
    {
        var limiter = new RequestStartLimiter(10, () => 0, (_, _) => Task.CompletedTask);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => limiter.WaitAsync(cancellation.Token));
        await limiter.WaitAsync();

        Assert.Equal(0, limiter.TotalWaitMilliseconds);
    }

    [Fact]
    public async Task TotalWaitMilliseconds_ThreeCallsAtTenPerSecond_SumsAssignedWaits()
    {
        var limiter = new RequestStartLimiter(10, () => 0, (_, _) => Task.CompletedTask);

        await limiter.WaitAsync();
        await limiter.WaitAsync();
        await limiter.WaitAsync();

        Assert.Equal(300, limiter.TotalWaitMilliseconds);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    public void Constructor_NonPositiveRate_Throws(double rate)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new RequestStartLimiter(rate));
    }
}

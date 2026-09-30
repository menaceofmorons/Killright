using Killright.UI.Scan;
using Xunit;

namespace Killright.UI.Tests.Scan;

public sealed class BoundedConcurrentRunnerTests
{
    [Fact]
    public async Task RunAsync_ItemsFinishingOutOfOrder_ReturnsResultsInListOrder()
    {
        var items = new[] { "Lukas Naarii", "T'ral Vsengne", "syMptom NZ" };
        var delays = new Dictionary<string, int> { ["Lukas Naarii"] = 60, ["T'ral Vsengne"] = 30, ["syMptom NZ"] = 1 };

        var results = await BoundedConcurrentRunner.RunAsync(
            items,
            8,
            async (item, _, token) =>
            {
                await Task.Delay(delays[item], token);
                return item.ToUpperInvariant();
            },
            (_, _) => "fault");

        Assert.Equal(["LUKAS NAARII", "T'RAL VSENGNE", "SYMPTOM NZ"], results);
    }

    [Fact]
    public async Task RunAsync_MoreItemsThanLimit_NeverExceedsLimit()
    {
        var inFlight = 0;
        var peak = 0;
        var gate = new object();

        var results = await BoundedConcurrentRunner.RunAsync(
            Enumerable.Range(0, 20).ToList(),
            4,
            async (item, _, token) =>
            {
                lock (gate)
                {
                    inFlight++;
                    peak = Math.Max(peak, inFlight);
                }

                await Task.Delay(10, token);

                lock (gate)
                    inFlight--;

                return item;
            },
            (_, _) => -1);

        Assert.Equal(Enumerable.Range(0, 20), results);
        Assert.InRange(peak, 2, 4);
    }

    [Fact]
    public async Task RunAsync_OneItemThrows_OthersKeepTheirResults()
    {
        var results = await BoundedConcurrentRunner.RunAsync(
            new[] { 1, 2, 3 },
            2,
            (item, _, _) => item == 2 ? throw new InvalidOperationException("boom") : Task.FromResult(item * 10),
            (_, _) => -1);

        Assert.Equal([10, -1, 30], results);
    }

    [Fact]
    public async Task RunAsync_LimitBelowOne_TreatedAsOne()
    {
        var results = await BoundedConcurrentRunner.RunAsync(
            new[] { 1, 2 },
            0,
            (item, _, _) => Task.FromResult(item),
            (_, _) => -1);

        Assert.Equal([1, 2], results);
    }

    [Fact]
    public async Task RunAsync_EmptyList_ReturnsEmpty()
    {
        var results = await BoundedConcurrentRunner.RunAsync(
            Array.Empty<int>(),
            8,
            (item, _, _) => Task.FromResult(item),
            (_, _) => -1);

        Assert.Empty(results);
    }

    [Fact]
    public async Task RunAsync_CancelledWhileWaiting_Throws()
    {
        using var cancellation = new CancellationTokenSource();

        var run = BoundedConcurrentRunner.RunAsync(
            new[] { 1, 2, 3 },
            1,
            async (item, _, token) =>
            {
                await Task.Delay(Timeout.Infinite, token);
                return item;
            },
            (_, _) => -1,
            cancellation.Token);

        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
    }
}

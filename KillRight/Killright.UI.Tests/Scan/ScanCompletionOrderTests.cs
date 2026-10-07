using Killright.UI.Scan;
using Xunit;

namespace Killright.UI.Tests.Scan;

public sealed class ScanCompletionOrderTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task CompletedScan_AppliesGridThenCommitsPostEngineRows()
    {
        var steps = new List<string>();
        var coordinator = Coordinator(steps, afterGrid: _ => { });

        await coordinator.Submit(["Lukas Naarii"]).WaitAsync(Timeout);

        Assert.Equal(["grid:Lukas Naarii", "post_engine:Lukas Naarii"], steps);
    }

    [Fact]
    public async Task ScanCancelledBetweenGridAndPostEngineWrite_CommitsNoPostEngineRows()
    {
        var steps = new List<string>();
        ScanCoordinator? coordinator = null;
        coordinator = Coordinator(steps, afterGrid: _ => coordinator!.CancelCurrent());

        await coordinator.Submit(["Lukas Naarii"]).WaitAsync(Timeout);

        Assert.Equal(["grid:Lukas Naarii"], steps);
    }

    [Fact]
    public async Task NextScan_StartsOnlyAfterThePreviousScansPostEngineWrite()
    {
        var steps = new List<string>();
        var gridApplied = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releasePostEngine = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var coordinator = new ScanCoordinator(
            async (names, _, context) =>
            {
                lock (steps)
                    steps.Add($"start:{names[0]}");

                lock (steps)
                    steps.Add($"grid:{names[0]}");

                if (names[0] == "Lukas Naarii")
                {
                    gridApplied.SetResult();
                    await releasePostEngine.Task;
                }

                context.Token.ThrowIfCancellationRequested();

                lock (steps)
                    steps.Add($"post_engine:{names[0]}");
            },
            () => { },
            () => { });

        var first = coordinator.Submit(["Lukas Naarii"]);
        await gridApplied.Task.WaitAsync(Timeout);

        var second = coordinator.Submit(["T'ral Vsengne"]);
        await Task.Delay(100);

        lock (steps)
            Assert.DoesNotContain("start:T'ral Vsengne", steps);

        releasePostEngine.SetResult();
        await Task.WhenAll(first, second).WaitAsync(Timeout);

        Assert.Equal(
            ["start:Lukas Naarii", "grid:Lukas Naarii", "start:T'ral Vsengne", "grid:T'ral Vsengne", "post_engine:T'ral Vsengne"],
            steps);
    }

    private static ScanCoordinator Coordinator(List<string> steps, Action<ScanContext> afterGrid) =>
        new(
            (names, _, context) =>
            {
                if (context.IsCurrent())
                    steps.Add($"grid:{names[0]}");

                afterGrid(context);
                context.Token.ThrowIfCancellationRequested();
                steps.Add($"post_engine:{names[0]}");
                return Task.CompletedTask;
            },
            () => { },
            () => { });
}

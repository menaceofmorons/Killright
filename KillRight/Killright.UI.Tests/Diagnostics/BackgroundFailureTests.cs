using Killright.UI.Diagnostics;
using Xunit;

namespace Killright.UI.Tests.Diagnostics;

public sealed class InfoSheetLoadTests
{
    [Fact]
    public async Task RunAsync_FailingLoad_IsRecordedNotLost()
    {
        var recorded = new List<string>();

        await BackgroundFailureRecorder.RunAsync(
            () => Task.FromException(new InvalidOperationException("load boom")),
            "Info Sheet load",
            recorded.Add);

        var line = Assert.Single(recorded);
        Assert.Contains("Info Sheet load failed", line);
        Assert.Contains("load boom", line);
    }

    [Fact]
    public async Task RunAsync_ThrowingBeforeFirstAwait_IsRecorded()
    {
        var recorded = new List<string>();

        await BackgroundFailureRecorder.RunAsync(
            () => throw new InvalidOperationException("sync boom"),
            "Startup purge",
            recorded.Add);

        Assert.Contains("sync boom", Assert.Single(recorded));
    }

    [Fact]
    public async Task RunAsync_SuccessfulWork_RecordsNothing()
    {
        var recorded = new List<string>();

        await BackgroundFailureRecorder.RunAsync(() => Task.CompletedTask, "SDE update check", recorded.Add);

        Assert.Empty(recorded);
    }
}

[Collection("UnobservedTaskException")]
public sealed class UnobservedFailureTests
{
    [Fact]
    public void UnobservedTaskFault_IsWrittenToTheRecorder()
    {
        var recorded = new List<string>();
        var marker = Guid.NewGuid().ToString("N");

        void Handler(object? sender, UnobservedTaskExceptionEventArgs e)
        {
            if (e.Exception.ToString().Contains(marker))
                BackgroundFailureRecorder.RecordUnobserved(e, recorded.Add);
        }

        TaskScheduler.UnobservedTaskException += Handler;

        try
        {
            RaiseUnobservedFault(marker);

            for (var attempt = 0; attempt < 20 && recorded.Count == 0; attempt++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                Thread.Sleep(50);
            }
        }
        finally
        {
            TaskScheduler.UnobservedTaskException -= Handler;
        }

        var line = Assert.Single(recorded);
        Assert.Contains("Unobserved task exception", line);
        Assert.Contains(marker, line);
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static void RaiseUnobservedFault(string marker)
    {
        _ = Task.Run(() => throw new InvalidOperationException(marker));
    }
}

using Killright.UI.Analysis;
using Xunit;

namespace Killright.UI.Tests.Analysis;

public sealed class KillrightEngineRuntimeLifecycleTests
{
    private const nint FakeHandle = 4242;

    [Fact]
    public async Task AnalyzePilotAsync_Call_RunsOffTheCallersThread()
    {
        var native = new KillrightEngineRuntimeTests.FakeNativeLibrary();
        using var runtime = new KillrightEngineRuntime("x.dll", "s.json", native, _ => { });

        await runtime.AnalyzePilotAsync("{}");

        Assert.Equal(1, native.CallCount);
        Assert.NotEqual(Environment.CurrentManagedThreadId, native.CallThreadId);
    }

    [Fact]
    public async Task Dispose_CallInFlight_WaitsForItBeforeFreeingLibrary()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var native = new KillrightEngineRuntimeTests.FakeNativeLibrary
        {
            OnCall = () =>
            {
                entered.Set();
                release.Wait(TimeSpan.FromSeconds(10));
            }
        };
        var runtime = new KillrightEngineRuntime("x.dll", "s.json", native, _ => { });

        var call = runtime.AnalyzePilotAsync("{}");
        Assert.True(entered.Wait(TimeSpan.FromSeconds(10)));

        var dispose = Task.Run(runtime.Dispose);
        await Task.Delay(200);

        Assert.False(dispose.IsCompleted);
        Assert.Empty(native.Freed);

        release.Set();
        await call;
        await dispose.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal([FakeHandle], native.Freed);
        Assert.Equal(1, native.ShutdownCalls);
    }

    [Fact]
    public async Task Dispose_ConcurrentCalls_FreeLibraryOnce()
    {
        var native = new KillrightEngineRuntimeTests.FakeNativeLibrary();
        var runtime = new KillrightEngineRuntime("x.dll", "s.json", native, _ => { });

        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(runtime.Dispose)));

        Assert.Equal(1, native.ShutdownCalls);
        Assert.Equal([FakeHandle], native.Freed);
    }

    [Fact]
    public async Task AnalyzePilotAsync_AfterDispose_FailsSafelyWithoutReachingNative()
    {
        var native = new KillrightEngineRuntimeTests.FakeNativeLibrary();
        var runtime = new KillrightEngineRuntime("x.dll", "s.json", native, _ => { });
        runtime.Dispose();

        var response = await runtime.AnalyzePilotAsync("{}");

        Assert.Equal("{\"character_id\":0,\"failure\":\"missing_runtime\"}", response);
        Assert.Equal(0, native.CallCount);
    }
}

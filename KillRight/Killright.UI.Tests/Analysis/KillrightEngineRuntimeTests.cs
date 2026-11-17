using System.Runtime.InteropServices;
using Killright.UI.Analysis;
using Xunit;

namespace Killright.UI.Tests.Analysis;

public sealed class KillrightEngineRuntimeTests
{
    private const nint FakeHandle = 4242;

    [Fact]
    public void Constructor_MissingLibrary_ReportsMissingRuntimeWithoutThrowing()
    {
        var native = new FakeNativeLibrary { LoadThrows = true };
        var log = new List<string>();

        using var runtime = new KillrightEngineRuntime("x.dll", "s.json", native, log.Add);

        Assert.False(runtime.IsAvailable);
        Assert.Equal("missing_runtime", runtime.InitializeFailureReason);
        Assert.False(runtime.InitializeFailedOnSettings);
        Assert.Single(log);
    }

    [Fact]
    public void Constructor_MissingExport_ReleasesHandleAndReportsMissingRuntime()
    {
        var native = new FakeNativeLibrary { MissingExport = "killright_shutdown" };
        var log = new List<string>();

        using var runtime = new KillrightEngineRuntime("x.dll", "s.json", native, log.Add);

        Assert.False(runtime.IsAvailable);
        Assert.Equal("missing_runtime", runtime.InitializeFailureReason);
        Assert.Equal([FakeHandle], native.Freed);
        Assert.Single(log);
    }

    [Fact]
    public void Constructor_PassesSettingsPathToInitialise()
    {
        var native = new FakeNativeLibrary();

        using var runtime = new KillrightEngineRuntime("x.dll", "C:/cfg/settings.json", native, _ => { });

        Assert.True(runtime.IsAvailable);
        Assert.Equal("C:/cfg/settings.json", native.ReceivedSettingsPath);
        Assert.Empty(native.Freed);
    }

    [Fact]
    public void Constructor_InitialiseReturnsSmallPointer_TreatedAsNoResponseAndNotFreed()
    {
        var native = new FakeNativeLibrary { InitializePointer = 1 };

        using var runtime = new KillrightEngineRuntime("x.dll", "s.json", native, _ => { });

        Assert.False(runtime.IsAvailable);
        Assert.Equal("missing_runtime", runtime.InitializeFailureReason);
        Assert.Empty(native.FreedStrings);
    }

    [Fact]
    public void Constructor_ContractVersionMismatch_RefusesWithReasonAndReleasesHandle()
    {
        var native = new FakeNativeLibrary { InitializeJson = "{\"success\":true,\"contract_version\":2}" };
        var log = new List<string>();

        using var runtime = new KillrightEngineRuntime("x.dll", "s.json", native, log.Add);

        Assert.False(runtime.IsAvailable);
        Assert.Equal("contract_version_mismatch", runtime.InitializeFailureReason);
        Assert.False(runtime.InitializeFailedOnSettings);
        Assert.Equal([FakeHandle], native.Freed);
        Assert.Equal(1, native.ShutdownCalls);
        Assert.Contains("2", Assert.Single(log));
    }

    [Fact]
    public void Constructor_SettingsFailure_StopsStartup()
    {
        var native = new FakeNativeLibrary
        {
            InitializeJson = "{\"success\":false,\"contract_version\":1,\"failure\":\"settings_failure\",\"failure_detail\":\"bad file\"}"
        };
        var log = new List<string>();

        using var runtime = new KillrightEngineRuntime("x.dll", "s.json", native, log.Add);

        Assert.False(runtime.IsAvailable);
        Assert.True(runtime.InitializeFailedOnSettings);
        Assert.Equal("settings_failure", runtime.InitializeFailureReason);
        Assert.Equal([FakeHandle], native.Freed);
        Assert.Contains("bad file", Assert.Single(log));
    }

    [Fact]
    public void Constructor_OtherInitialiseFailure_TakesEngineUnavailablePath()
    {
        var native = new FakeNativeLibrary
        {
            InitializeJson = "{\"success\":false,\"contract_version\":1,\"failure\":\"initialize_failure\",\"failure_detail\":\"boom\"}"
        };

        using var runtime = new KillrightEngineRuntime("x.dll", "s.json", native, _ => { });

        Assert.False(runtime.IsAvailable);
        Assert.False(runtime.InitializeFailedOnSettings);
        Assert.Equal("initialize_failure", runtime.InitializeFailureReason);
        Assert.Equal([FakeHandle], native.Freed);
    }

    [Fact]
    public async Task Fallbacks_UnavailableRuntime_ReturnEachCallsOwnShape()
    {
        var native = new FakeNativeLibrary { LoadThrows = true };
        using var runtime = new KillrightEngineRuntime("x.dll", "s.json", native, _ => { });

        Assert.Equal("{\"results\":[],\"failure\":\"missing_runtime\"}", await runtime.AnalyzePilotsAsync("{}"));
        Assert.Equal("{\"character_id\":0,\"failure\":\"missing_runtime\"}", await runtime.AnalyzePilotAsync("{}"));
        Assert.Equal("{\"character_id\":0,\"failure\":\"missing_runtime\"}", await runtime.DiagnoseThreatAsync("{}"));
        Assert.Equal("{\"character_id\":0,\"failure\":\"missing_runtime\"}", await runtime.DiagnoseGroupDetectionAsync("{}"));
    }

    [Fact]
    public async Task AnalyzePilotsAsync_NullNativeResponse_ReturnsBatchShape()
    {
        var native = new FakeNativeLibrary { CallResponseJson = null };
        using var runtime = new KillrightEngineRuntime("x.dll", "s.json", native, _ => { });

        Assert.Equal("{\"results\":[],\"failure\":\"missing_runtime\"}", await runtime.AnalyzePilotsAsync("{}"));
        Assert.Equal("{\"character_id\":0,\"failure\":\"missing_runtime\"}", await runtime.AnalyzePilotAsync("{}"));
    }

    [Fact]
    public async Task AnalyzePilotAsync_PanicResponse_RecordsCause()
    {
        var native = new FakeNativeLibrary
        {
            CallResponseJson = "{\"failure\":\"internal_error\",\"failure_detail\":\"index out of range\",\"character_id\":0}"
        };
        var log = new List<string>();
        using var runtime = new KillrightEngineRuntime("x.dll", "s.json", native, log.Add);

        var response = await runtime.AnalyzePilotAsync("{}");

        Assert.Contains("internal_error", response);
        Assert.Contains("index out of range", Assert.Single(log));
    }

    [Fact]
    public async Task AnalyzePilotAsync_NormalResponse_IsReturnedAndNotLogged()
    {
        var native = new FakeNativeLibrary { CallResponseJson = "{\"character_id\":7}" };
        var log = new List<string>();
        using var runtime = new KillrightEngineRuntime("x.dll", "s.json", native, log.Add);

        Assert.Equal("{\"character_id\":7}", await runtime.AnalyzePilotAsync("{}"));
        Assert.Empty(log);
    }

    [Fact]
    public void Dispose_AvailableRuntime_ShutsDownAndFreesHandleOnce()
    {
        var native = new FakeNativeLibrary();
        var runtime = new KillrightEngineRuntime("x.dll", "s.json", native, _ => { });

        runtime.Dispose();
        runtime.Dispose();

        Assert.Equal(1, native.ShutdownCalls);
        Assert.Equal([FakeHandle], native.Freed);
    }

    internal sealed class FakeNativeLibrary : INativeLibrary
    {
        private readonly List<nint> _allocated = [];

        public Action? OnCall { get; init; }

        public int CallThreadId { get; private set; }

        public int CallCount { get; private set; }

        public bool LoadThrows { get; init; }

        public string? MissingExport { get; init; }

        public string InitializeJson { get; init; } = "{\"success\":true,\"contract_version\":1}";

        public string? CallResponseJson { get; init; } = "{\"character_id\":0}";

        public string? ReceivedSettingsPath { get; private set; }

        public int ShutdownCalls { get; private set; }

        public nint? InitializePointer { get; init; }

        public List<nint> FreedStrings { get; } = [];

        public List<nint> Freed { get; } = [];

        public nint Load(string path)
        {
            if (LoadThrows)
                throw new DllNotFoundException(path);

            return FakeHandle;
        }

        public TDelegate GetExport<TDelegate>(nint handle, string name)
            where TDelegate : Delegate
        {
            if (name == MissingExport)
                throw new EntryPointNotFoundException(name);

            Delegate export = name switch
            {
                "killright_initialize" => new KillrightEngineRuntime.InitializeDelegate(argument =>
                {
                    ReceivedSettingsPath = Marshal.PtrToStringUTF8(argument);
                    return InitializePointer ?? Allocate(InitializeJson);
                }),
                "killright_shutdown" => new KillrightEngineRuntime.ShutdownDelegate(() => ShutdownCalls++),
                "killright_free_string" => new KillrightEngineRuntime.FreeStringDelegate(pointer => FreedStrings.Add(pointer)),
                _ => new KillrightEngineRuntime.JsonExportDelegate(_ =>
                {
                    CallCount++;
                    CallThreadId = Environment.CurrentManagedThreadId;
                    OnCall?.Invoke();
                    return Allocate(CallResponseJson);
                })
            };

            return (TDelegate)export;
        }

        public void Free(nint handle)
        {
            Freed.Add(handle);
        }

        private nint Allocate(string? json)
        {
            if (json is null)
                return nint.Zero;

            var pointer = Marshal.StringToCoTaskMemUTF8(json);
            _allocated.Add(pointer);
            return pointer;
        }
    }
}

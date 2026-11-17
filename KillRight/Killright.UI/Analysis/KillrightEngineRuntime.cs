using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Killright.Storage.Diagnostics;

namespace Killright.UI.Analysis;

public sealed class KillrightEngineRuntime : IKillrightEngineRuntime
{
    public const int ExpectedContractVersion = 1;

    private const string SettingsFailureCode = "settings_failure";
    private const string MissingRuntimeCode = "missing_runtime";
    private const string InternalErrorCode = "internal_error";
    private const string SingleFallbackJson = "{\"character_id\":0,\"failure\":\"missing_runtime\"}";
    private const string BatchFallbackJson = "{\"results\":[],\"failure\":\"missing_runtime\"}";

    private readonly INativeLibrary _native;
    private readonly Action<string> _logFailure;
    private nint _libraryHandle;
    private NativeExports? _exports;
    private bool _disposed;

    public bool IsAvailable { get; private set; }

    public string? InitializeFailureReason { get; private set; }

    public bool InitializeFailedOnSettings { get; private set; }

    public KillrightEngineRuntime(
        string dllPath,
        string settingsPath)
        : this(dllPath, settingsPath, SystemNativeLibrary.Instance, EngineFailureLog.Record)
    {
    }

    internal KillrightEngineRuntime(
        string dllPath,
        string settingsPath,
        INativeLibrary native,
        Action<string> logFailure)
    {
        _native = native;
        _logFailure = logFailure;

        try
        {
            _libraryHandle = _native.Load(dllPath);
            _exports = BindExports();
            IsAvailable = Initialize(settingsPath);
        }
        catch (Exception exception) when (
            exception is DllNotFoundException
                or BadImageFormatException
                or EntryPointNotFoundException
                or ArgumentException)
        {
            Fail(MissingRuntimeCode, exception.Message);
        }
        catch (Exception exception)
        {
            Fail("initialize_failure", exception.Message);
        }
    }

    public Task<string> AnalyzePilotAsync(
        string requestJson,
        CancellationToken cancellationToken = default,
        ScanTimings? timings = null,
        long? timingCharacterId = null)
    {
        return InvokeJsonExport(e => e.AnalyzePilot, SingleFallbackJson, requestJson, cancellationToken, timings, timingCharacterId);
    }

    public Task<string> AnalyzePilotsAsync(
        string requestJson,
        CancellationToken cancellationToken = default,
        ScanTimings? timings = null)
    {
        return InvokeJsonExport(e => e.AnalyzePilots, BatchFallbackJson, requestJson, cancellationToken, timings);
    }

    public Task<string> DiagnoseGroupDetectionAsync(
        string requestJson,
        CancellationToken cancellationToken = default)
    {
        return InvokeJsonExport(e => e.DiagnoseGroupDetection, SingleFallbackJson, requestJson, cancellationToken);
    }

    public Task<string> DiagnoseThreatAsync(
        string requestJson,
        CancellationToken cancellationToken = default)
    {
        return InvokeJsonExport(e => e.DiagnoseThreat, SingleFallbackJson, requestJson, cancellationToken);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_exports is null)
        {
            return;
        }

        try
        {
            _exports.Shutdown();
        }
        finally
        {
            ReleaseLibrary();
        }
    }

    private NativeExports BindExports()
    {
        return new NativeExports(
            GetExport<InitializeDelegate>("killright_initialize"),
            GetExport<JsonExportDelegate>("killright_analyze_pilot"),
            GetExport<JsonExportDelegate>("killright_analyze_pilots"),
            GetExport<JsonExportDelegate>("killright_diagnose_group_detection"),
            GetExport<JsonExportDelegate>("killright_diagnose_threat"),
            GetExport<ShutdownDelegate>("killright_shutdown"),
            GetExport<FreeStringDelegate>("killright_free_string"));
    }

    private bool Initialize(
        string settingsPath)
    {
        var exports = _exports!;
        var responseJson = CallInitialize(exports, settingsPath);

        if (responseJson is null)
        {
            Fail(MissingRuntimeCode, "Initialise returned no response.");
            return false;
        }

        InitializeResponse? response;

        try
        {
            response = JsonSerializer.Deserialize<InitializeResponse>(responseJson);
        }
        catch (JsonException exception)
        {
            Fail(MissingRuntimeCode, $"Initialise response was unreadable. {exception.Message}");
            return false;
        }

        if (response is null)
        {
            Fail(MissingRuntimeCode, "Initialise response was empty.");
            return false;
        }

        if (!response.success)
        {
            var failure = string.IsNullOrWhiteSpace(response.failure) ? "initialize_failure" : response.failure;
            InitializeFailedOnSettings = failure == SettingsFailureCode;
            Fail(failure, response.failure_detail);
            return false;
        }

        if (response.contract_version != ExpectedContractVersion)
        {
            ShutdownQuietly(exports);
            Fail(
                "contract_version_mismatch",
                $"engine contract version {response.contract_version}, expected {ExpectedContractVersion}.");
            return false;
        }

        return true;
    }

    private static string? CallInitialize(
        NativeExports exports,
        string settingsPath)
    {
        var pathBytes = Encoding.UTF8.GetBytes(settingsPath + "\0");
        var pathPointer = Marshal.AllocHGlobal(pathBytes.Length);

        try
        {
            Marshal.Copy(pathBytes, 0, pathPointer, pathBytes.Length);

            var responsePointer = exports.Initialize(pathPointer);

            if (responsePointer == nint.Zero)
            {
                return null;
            }

            try
            {
                return Marshal.PtrToStringUTF8(responsePointer);
            }
            finally
            {
                exports.FreeString(responsePointer);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(pathPointer);
        }
    }

    private void ShutdownQuietly(
        NativeExports exports)
    {
        try
        {
            exports.Shutdown();
        }
        catch (SEHException exception)
        {
            _logFailure($"Engine shutdown after refused initialise failed. {exception.Message}");
        }
    }

    private void Fail(
        string reason,
        string? detail)
    {
        InitializeFailureReason = reason;
        IsAvailable = false;
        _exports = null;
        ReleaseLibrary();

        _logFailure(
            string.IsNullOrWhiteSpace(detail)
                ? $"Engine initialise failed: {reason}."
                : $"Engine initialise failed: {reason}. {detail}");
    }

    private void ReleaseLibrary()
    {
        if (_libraryHandle == nint.Zero)
        {
            return;
        }

        var handle = _libraryHandle;
        _libraryHandle = nint.Zero;
        _native.Free(handle);
    }

    private Task<string> InvokeJsonExport(
        Func<NativeExports, JsonExportDelegate> selectExport,
        string fallbackJson,
        string requestJson,
        CancellationToken cancellationToken,
        ScanTimings? timings = null,
        long? timingCharacterId = null)
    {
        var exports = _exports;

        if (_disposed || !IsAvailable || exports is null)
        {
            return Task.FromResult(fallbackJson);
        }

        cancellationToken.ThrowIfCancellationRequested();

        var export = selectExport(exports);

        var requestBytes =
            Encoding.UTF8.GetBytes(requestJson + "\0");

        var requestPointer =
            Marshal.AllocHGlobal(requestBytes.Length);

        try
        {
            Marshal.Copy(
                requestBytes,
                0,
                requestPointer,
                requestBytes.Length);

            nint responsePointer;

            using (timings.Measure(ScanTimings.EngineLevel, "native_call", timingCharacterId))
            {
                responsePointer = export(requestPointer);
            }

            if (responsePointer == nint.Zero)
            {
                return Task.FromResult(fallbackJson);
            }

            try
            {
                var response = Marshal.PtrToStringUTF8(responsePointer) ?? fallbackJson;
                RecordPanicCause(response);
                return Task.FromResult(response);
            }
            finally
            {
                exports.FreeString(responsePointer);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(requestPointer);
        }
    }

    private void RecordPanicCause(
        string responseJson)
    {
        if (!responseJson.Contains(InternalErrorCode, StringComparison.Ordinal))
        {
            return;
        }

        try
        {
            var response = JsonSerializer.Deserialize<InitializeResponse>(responseJson);

            if (response?.failure == InternalErrorCode)
            {
                _logFailure($"Engine call panicked: {response.failure_detail ?? "no cause reported"}");
            }
        }
        catch (JsonException)
        {
            _logFailure("Engine call reported an internal error with an unreadable response.");
        }
    }

    private TDelegate GetExport<TDelegate>(
        string name)
        where TDelegate : Delegate
    {
        return _native.GetExport<TDelegate>(
            _libraryHandle,
            name);
    }

    private sealed record NativeExports(
        InitializeDelegate Initialize,
        JsonExportDelegate AnalyzePilot,
        JsonExportDelegate AnalyzePilots,
        JsonExportDelegate DiagnoseGroupDetection,
        JsonExportDelegate DiagnoseThreat,
        ShutdownDelegate Shutdown,
        FreeStringDelegate FreeString);

    private sealed class InitializeResponse
    {
        public bool success { get; set; }

        public int contract_version { get; set; }

        public string? failure { get; set; }

        public string? failure_detail { get; set; }
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate nint InitializeDelegate(
        nint settingsPath);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate nint JsonExportDelegate(
        nint requestJson);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate void ShutdownDelegate();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate void FreeStringDelegate(
        nint value);
}

internal interface INativeLibrary
{
    nint Load(string path);

    TDelegate GetExport<TDelegate>(nint handle, string name)
        where TDelegate : Delegate;

    void Free(nint handle);
}

internal sealed class SystemNativeLibrary : INativeLibrary
{
    public static SystemNativeLibrary Instance { get; } = new();

    public nint Load(string path) => NativeLibrary.Load(path);

    public TDelegate GetExport<TDelegate>(nint handle, string name)
        where TDelegate : Delegate
    {
        return Marshal.GetDelegateForFunctionPointer<TDelegate>(NativeLibrary.GetExport(handle, name));
    }

    public void Free(nint handle) => NativeLibrary.Free(handle);
}

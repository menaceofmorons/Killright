using System.Runtime.InteropServices;
using System.Text;

namespace Killright.UI.Analysis;

public sealed class KillrightEngineRuntime : IKillrightEngineRuntime
{
    private readonly nint _libraryHandle;
    private readonly InitializeDelegate _initialize;
    private readonly JsonExportDelegate _analyzePilot;
    private readonly JsonExportDelegate _diagnoseGroupDetection;
    private readonly JsonExportDelegate _diagnoseThreat;
    private readonly ShutdownDelegate _shutdown;
    private readonly FreeStringDelegate _freeString;
    private bool _disposed;

    public bool IsAvailable { get; }

    public KillrightEngineRuntime(
        string dllPath,
        string databasePath)
    {
        Environment.SetEnvironmentVariable(
            "PILOTINTEL_DB_PATH",
            databasePath);

        _libraryHandle = NativeLibrary.Load(dllPath);

        _initialize = GetExport<InitializeDelegate>(
            "pintel_initialize");

        _analyzePilot = GetExport<JsonExportDelegate>(
            "pintel_analyze_pilot");

        _diagnoseGroupDetection = GetExport<JsonExportDelegate>(
            "pintel_diagnose_group_detection");

        _diagnoseThreat = GetExport<JsonExportDelegate>(
            "pintel_diagnose_threat");

        _shutdown = GetExport<ShutdownDelegate>(
            "pintel_shutdown");

        _freeString = GetExport<FreeStringDelegate>(
            "pintel_free_string");

        IsAvailable = _initialize() == 1;
    }

    public Task<string> AnalyzePilotAsync(
        string requestJson,
        CancellationToken cancellationToken = default)
    {
        return InvokeJsonExport(_analyzePilot, requestJson, cancellationToken);
    }

    public Task<string> DiagnoseGroupDetectionAsync(
        string requestJson,
        CancellationToken cancellationToken = default)
    {
        return InvokeJsonExport(_diagnoseGroupDetection, requestJson, cancellationToken);
    }

    public Task<string> DiagnoseThreatAsync(
        string requestJson,
        CancellationToken cancellationToken = default)
    {
        return InvokeJsonExport(_diagnoseThreat, requestJson, cancellationToken);
    }

    private Task<string> InvokeJsonExport(
        JsonExportDelegate export,
        string requestJson,
        CancellationToken cancellationToken)
    {
        if (_disposed || !IsAvailable)
        {
            return Task.FromResult(
                "{\"character_id\":0,\"failure\":\"missing_runtime\"}");
        }

        cancellationToken.ThrowIfCancellationRequested();

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

            var responsePointer =
                export(requestPointer);

            if (responsePointer == nint.Zero)
            {
                return Task.FromResult(
                    "{\"character_id\":0,\"failure\":\"missing_runtime\"}");
            }

            try
            {
                return Task.FromResult(
                    Marshal.PtrToStringUTF8(responsePointer)
                    ?? "{\"character_id\":0,\"failure\":\"missing_runtime\"}");
            }
            finally
            {
                _freeString(responsePointer);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(requestPointer);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        try
        {
            _shutdown();
        }
        finally
        {
            NativeLibrary.Free(_libraryHandle);
        }
    }

    private TDelegate GetExport<TDelegate>(
        string name)
        where TDelegate : Delegate
    {
        var pointer = NativeLibrary.GetExport(
            _libraryHandle,
            name);

        return Marshal.GetDelegateForFunctionPointer<TDelegate>(pointer);
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int InitializeDelegate();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate nint JsonExportDelegate(
        nint requestJson);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void ShutdownDelegate();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void FreeStringDelegate(
        nint value);
}

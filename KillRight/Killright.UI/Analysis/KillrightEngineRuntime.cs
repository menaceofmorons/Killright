using System.Runtime.InteropServices;
using System.Text;

namespace Killright.UI.Analysis;

public sealed class KillrightEngineRuntime : IKillrightEngineRuntime
{
    private readonly nint _libraryHandle;
    private readonly InitializeDelegate _initialize;
    private readonly AnalyzePilotDelegate _analyzePilot;
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

        _analyzePilot = GetExport<AnalyzePilotDelegate>(
            "pintel_analyze_pilot");

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
        if (_disposed || !IsAvailable)
        {
            return Task.FromResult(
                "{\"character_id\":0,\"recent_style\":\"Unknown\"}");
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
                _analyzePilot(requestPointer);

            if (responsePointer == nint.Zero)
            {
                return Task.FromResult(
                    "{\"character_id\":0,\"recent_style\":\"Unknown\"}");
            }

            try
            {
                return Task.FromResult(
                    Marshal.PtrToStringUTF8(responsePointer)
                    ?? "{\"character_id\":0,\"recent_style\":\"Unknown\"}");
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
    private delegate nint AnalyzePilotDelegate(
        nint requestJson);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void ShutdownDelegate();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void FreeStringDelegate(
        nint value);
}
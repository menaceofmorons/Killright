using Killright.Storage.Diagnostics;

namespace Killright.UI.Analysis;

public interface IKillrightEngineRuntime : IDisposable
{
    bool IsAvailable { get; }

    Task<string> AnalyzePilotAsync(
        string requestJson,
        CancellationToken cancellationToken = default,
        ScanTimings? timings = null,
        long? timingCharacterId = null);

    Task<string> AnalyzePilotsAsync(
        string requestJson,
        CancellationToken cancellationToken = default,
        ScanTimings? timings = null)
    {
        return Task.FromResult("{\"results\":[],\"failure\":\"missing_runtime\"}");
    }

    Task<string> DiagnoseGroupDetectionAsync(
        string requestJson,
        CancellationToken cancellationToken = default);

    Task<string> DiagnoseThreatAsync(
        string requestJson,
        CancellationToken cancellationToken = default);
}

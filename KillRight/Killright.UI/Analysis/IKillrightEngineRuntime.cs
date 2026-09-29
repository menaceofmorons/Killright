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

    Task<string> DiagnoseGroupDetectionAsync(
        string requestJson,
        CancellationToken cancellationToken = default);

    Task<string> DiagnoseThreatAsync(
        string requestJson,
        CancellationToken cancellationToken = default);
}

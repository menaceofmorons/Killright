using Killright.Storage.Diagnostics;

namespace Killright.Storage.Engine;

public interface IEngineInputReader
{
    Task<IReadOnlyList<EnginePilotInputsResult>> ReadPilotInputsAsync(
        IReadOnlyList<long> characterIds,
        ScanTimings? timings = null,
        CancellationToken cancellationToken = default);

    Task<EngineGroupInputsResult> ReadGroupInputsAsync(
        IReadOnlyList<long> scannedCharacterIds,
        ScanTimings? timings = null,
        CancellationToken cancellationToken = default);
}

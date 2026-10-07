using Killright.Storage.Diagnostics;
using Killright.Storage.Engine;

namespace Killright.UI.Tests.Analysis;

internal sealed class FakeEngineInputReader : IEngineInputReader
{
    private readonly IReadOnlyDictionary<long, string> _pilotFailures;
    private readonly string? _groupFailure;

    public FakeEngineInputReader(IReadOnlyDictionary<long, string>? pilotFailures = null, string? groupFailure = null)
    {
        _pilotFailures = pilotFailures ?? new Dictionary<long, string>();
        _groupFailure = groupFailure;
    }

    public int PilotReads { get; private set; }

    public int GroupReads { get; private set; }

    public IReadOnlyList<long> LastPilotIds { get; private set; } = Array.Empty<long>();

    public static EnginePilotInputs PilotInputs(long characterId) => new(
        characterId,
        [
            new EngineKillmailRow(
                1, "hash1", characterId, "2026-09-20T00:00:00+00:00", false, 2, false, 587, 30000142, null, false,
                "2026-09-20T00:00:00+00:00")
        ],
        new EngineStatisticsRow(
            characterId, 120, 30, 0.25, 4.0, 12, 2, "Gang", "2026-09-20T00:00:00+00:00", false, 0),
        new EngineIdentityRow(
            "LUKAS NAARII", characterId, "Lukas Naarii", "Partial", 1.5, 98000001, null, null, null, null, null,
            "2026-09-20T00:00:00+00:00"),
        "2026-09-08T00:00:00+00:00");

    public Task<IReadOnlyList<EnginePilotInputsResult>> ReadPilotInputsAsync(
        IReadOnlyList<long> characterIds,
        ScanTimings? timings = null,
        CancellationToken cancellationToken = default)
    {
        PilotReads++;
        LastPilotIds = characterIds.ToList();

        IReadOnlyList<EnginePilotInputsResult> results = characterIds
            .Select(characterId => _pilotFailures.TryGetValue(characterId, out var reason)
                ? new EnginePilotInputsResult(characterId, null, reason)
                : new EnginePilotInputsResult(characterId, PilotInputs(characterId), null))
            .ToList();

        return Task.FromResult(results);
    }

    public Task<EngineGroupInputsResult> ReadGroupInputsAsync(
        IReadOnlyList<long> scannedCharacterIds,
        ScanTimings? timings = null,
        CancellationToken cancellationToken = default)
    {
        GroupReads++;

        if (_groupFailure is not null)
            return Task.FromResult(new EngineGroupInputsResult(null, _groupFailure));

        return Task.FromResult(new EngineGroupInputsResult(
            new EngineGroupInputs(
                [new EngineAttackerEvidenceRow(901, scannedCharacterIds[0], 98000001, null, "2026-09-20T00:00:00+00:00", 2)],
                Array.Empty<EngineAttackerEvidenceRow>(),
                Array.Empty<EngineIdentityRow>(),
                new long[] { 1000001 }),
            null));
    }
}

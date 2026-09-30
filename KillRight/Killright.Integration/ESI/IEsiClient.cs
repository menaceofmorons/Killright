using Killright.Core.Models;
using Killright.Shared;

namespace Killright.Integration.Esi;

public interface IEsiClient
{
    long RequestCount { get; }
    Task<Pilot> ResolvePilotAsync(string exactPilotName, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Pilot>> ResolvePilotsAsync(IEnumerable<string> exactPilotNames, CancellationToken cancellationToken = default);
    Task<EsiResolvedIdentity?> ResolveEntityByExactNameAsync(string exactName, IgnoreEntryType type, CancellationToken cancellationToken = default);
    Task<IReadOnlyDictionary<string, EsiNameLookup>> ResolveNamesAsync(IReadOnlyList<string> exactNames, CancellationToken cancellationToken = default);
    Task<IReadOnlyDictionary<long, EsiAffiliation>> GetAffiliationsAsync(IReadOnlyList<long> characterIds, CancellationToken cancellationToken = default);
    Task<IReadOnlyDictionary<long, string>> GetEntityNamesAsync(IReadOnlyList<long> entityIds, CancellationToken cancellationToken = default);
    Task<EsiCharacterDetails?> GetCharacterDetailsAsync(long characterId, CancellationToken cancellationToken = default);
}

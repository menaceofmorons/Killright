using Killright.Core.Models;
using Killright.Shared;

namespace Killright.Integration.Esi;

public interface IEsiClient
{
    Task<Pilot> ResolvePilotAsync(string exactPilotName, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Pilot>> ResolvePilotsAsync(IEnumerable<string> exactPilotNames, CancellationToken cancellationToken = default);
    Task<EsiResolvedIdentity?> ResolveEntityByExactNameAsync(string exactName, IgnoreEntryType type, CancellationToken cancellationToken = default);
}

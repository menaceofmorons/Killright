using PilotIntel.Core.Models;

namespace PilotIntel.Integration.Esi;

public interface IEsiClient
{
    Task<Pilot> ResolvePilotAsync(string exactPilotName, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Pilot>> ResolvePilotsAsync(IEnumerable<string> exactPilotNames, CancellationToken cancellationToken = default);
}

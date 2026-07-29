using PilotIntel.Core.Models;

namespace PilotIntel.Storage.Identity;

public interface IPilotIdentityCache
{
    Task<Pilot?> GetAsync(string inputName, TimeSpan maximumAge, CancellationToken cancellationToken = default);
    Task UpsertAsync(Pilot pilot, CancellationToken cancellationToken = default);
}
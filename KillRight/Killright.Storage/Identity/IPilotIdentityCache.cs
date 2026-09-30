using Killright.Core.Models;
using Killright.Storage.Database;

namespace Killright.Storage.Identity;

public interface IPilotIdentityCache
{
    Task<Pilot?> GetAsync(string inputName, TimeSpan maximumAge, CancellationToken cancellationToken = default);
    Task UpsertAsync(Pilot pilot, CancellationToken cancellationToken = default);
    Task<DateOnly?> GetBirthdayAsync(string inputName, CancellationToken cancellationToken = default);
    Task<PilotIdentityCacheRecord?> GetRecordAsync(string inputName, CancellationToken cancellationToken = default);
    Task UpsertRecordAsync(PilotIdentityCacheRecord record, CancellationToken cancellationToken = default);

    async Task<IReadOnlyDictionary<string, PilotIdentityCacheRecord>> GetRecordsAsync(
        IReadOnlyCollection<string> inputNames,
        ScanDatabaseSession? session = null,
        CancellationToken cancellationToken = default)
    {
        var records = new Dictionary<string, PilotIdentityCacheRecord>(StringComparer.Ordinal);

        foreach (var inputName in inputNames)
        {
            try
            {
                var record = await GetRecordAsync(inputName, cancellationToken);

                if (record is not null)
                    records[PilotIdentityCacheRecord.NormalizeInputName(inputName)] = record;
            }
            catch
            {
            }
        }

        return records;
    }
}

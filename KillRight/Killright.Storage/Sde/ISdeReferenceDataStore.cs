using Killright.Shared.Sde;

namespace Killright.Storage.Sde;

public interface ISdeReferenceDataStore
{
    string? GetTypeName(long typeId);

    string? GetSolarSystemName(long systemId);

    bool IsNpcCorporation(long corporationId);

    IReadOnlySet<long> GetNpcCorporationIds();

    string? GetFactionName(long factionId);

    Task<bool> HasReferenceDataAsync(
        CancellationToken cancellationToken = default);

    Task<SdeMetadata> GetMetadataAsync(
        CancellationToken cancellationToken = default);

    Task ReplaceTablesAsync(
        SdeReplacementData data,
        CancellationToken cancellationToken = default);

    Task RecordCheckAsync(
        DateTimeOffset attemptedUtc,
        string checkResult,
        bool succeeded,
        CancellationToken cancellationToken = default);
}

using Killright.Shared.Sde;

namespace Killright.Storage.Sde;

public interface ISdeReferenceDataStore
{
    string? GetTypeName(long typeId);

    string? GetSolarSystemName(long systemId);

    bool IsNpcCorporation(long corporationId);

    IReadOnlySet<long> GetNpcCorporationIds();

    Task<SdeMetadata> GetMetadataAsync(
        CancellationToken cancellationToken = default);

    Task ReplaceTablesAsync(
        SdeReplacementData data,
        CancellationToken cancellationToken = default);

    Task RecordCheckAsync(
        DateTimeOffset checkedUtc,
        string checkResult,
        CancellationToken cancellationToken = default);
}

namespace Killright.Shared.Sde;

public sealed record SdeReplacementData(
    IReadOnlyList<SdeType> Types,
    IReadOnlyList<SdeSolarSystem> SolarSystems,
    IReadOnlyList<long> NpcCorporationIds,
    long BuildNumber,
    DateTimeOffset UpdatedUtc);

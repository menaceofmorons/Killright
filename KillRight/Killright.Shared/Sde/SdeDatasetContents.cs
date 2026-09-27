namespace Killright.Shared.Sde;

public sealed record SdeDatasetContents(
    IReadOnlyList<SdeType> Types,
    IReadOnlyList<SdeSolarSystem> SolarSystems,
    IReadOnlyList<long> NpcCorporationIds);

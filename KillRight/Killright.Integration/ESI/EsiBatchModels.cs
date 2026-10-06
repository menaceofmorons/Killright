namespace Killright.Integration.Esi;

public enum EsiLookupOutcome
{
    Matched,
    NoMatch,
    Failed
}

public sealed record EsiNameLookup(EsiLookupOutcome Outcome, long? CharacterId, string? CharacterName)
{
    public static EsiNameLookup Matched(long characterId, string characterName) => new(EsiLookupOutcome.Matched, characterId, characterName);

    public static EsiNameLookup NoMatch { get; } = new(EsiLookupOutcome.NoMatch, null, null);

    public static EsiNameLookup Failed { get; } = new(EsiLookupOutcome.Failed, null, null);
}

public sealed record EsiAffiliation(long CharacterId, long CorporationId, long? AllianceId);

public sealed record EsiCharacterDetails(
    long CharacterId,
    string Name,
    long CorporationId,
    long? AllianceId,
    double? SecurityStatus,
    DateOnly? Birthday,
    long? FactionId = null);

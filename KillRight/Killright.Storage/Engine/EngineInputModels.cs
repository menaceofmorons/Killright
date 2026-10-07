using System.Text.Json.Serialization;

namespace Killright.Storage.Engine;

public sealed record EngineKillmailRow(
    [property: JsonPropertyName("killmail_id")] long KillmailId,
    [property: JsonPropertyName("killmail_hash")] string? KillmailHash,
    [property: JsonPropertyName("character_id")] long CharacterId,
    [property: JsonPropertyName("kill_time_utc")] string KillTimeUtc,
    [property: JsonPropertyName("is_loss")] bool IsLoss,
    [property: JsonPropertyName("attacker_count")] int AttackerCount,
    [property: JsonPropertyName("is_solo")] bool IsSolo,
    [property: JsonPropertyName("ship_type_id")] long? ShipTypeId,
    [property: JsonPropertyName("system_id")] long? SystemId,
    [property: JsonPropertyName("location_id")] long? LocationId,
    [property: JsonPropertyName("is_npc")] bool IsNpc,
    [property: JsonPropertyName("cached_at_utc")] string CachedAtUtc);

public sealed record EngineStatisticsRow(
    [property: JsonPropertyName("character_id")] long CharacterId,
    [property: JsonPropertyName("ships_destroyed")] int ShipsDestroyed,
    [property: JsonPropertyName("solo_kills")] int SoloKills,
    [property: JsonPropertyName("solo_ratio")] double SoloRatio,
    [property: JsonPropertyName("avg_gang_size")] double AvgGangSize,
    [property: JsonPropertyName("ships_lost")] int ShipsLost,
    [property: JsonPropertyName("solo_losses")] int SoloLosses,
    [property: JsonPropertyName("general_style")] string GeneralStyle,
    [property: JsonPropertyName("checked_at_utc")] string CheckedAtUtc,
    [property: JsonPropertyName("no_history_marker")] bool NoHistoryMarker,
    [property: JsonPropertyName("pod_losses")] int PodLosses);

public sealed record EngineIdentityRow(
    [property: JsonPropertyName("input_name")] string InputName,
    [property: JsonPropertyName("character_id")] long? CharacterId,
    [property: JsonPropertyName("character_name")] string? CharacterName,
    [property: JsonPropertyName("verify_status")] string VerifyStatus,
    [property: JsonPropertyName("security_status")] double? SecurityStatus,
    [property: JsonPropertyName("corporation_id")] long? CorporationId,
    [property: JsonPropertyName("corporation_name")] string? CorporationName,
    [property: JsonPropertyName("corporation_ticker")] string? CorporationTicker,
    [property: JsonPropertyName("alliance_id")] long? AllianceId,
    [property: JsonPropertyName("alliance_name")] string? AllianceName,
    [property: JsonPropertyName("alliance_ticker")] string? AllianceTicker,
    [property: JsonPropertyName("cached_at_utc")] string CachedAtUtc);

public sealed record EngineAttackerEvidenceRow(
    [property: JsonPropertyName("killmail_id")] long KillmailId,
    [property: JsonPropertyName("character_id")] long CharacterId,
    [property: JsonPropertyName("corporation_id")] long? CorporationId,
    [property: JsonPropertyName("alliance_id")] long? AllianceId,
    [property: JsonPropertyName("kill_time_utc")] string KillTimeUtc,
    [property: JsonPropertyName("unique_attacker_count")] long UniqueAttackerCount);

public sealed record EnginePilotInputs(
    [property: JsonPropertyName("character_id")] long CharacterId,
    [property: JsonPropertyName("killmails")] IReadOnlyList<EngineKillmailRow> Killmails,
    [property: JsonPropertyName("statistics")] EngineStatisticsRow? Statistics,
    [property: JsonPropertyName("identity")] EngineIdentityRow? Identity,
    [property: JsonPropertyName("coverage_start_utc")] string? CoverageStartUtc);

public sealed record EngineGroupInputs(
    [property: JsonPropertyName("direct_evidence")] IReadOnlyList<EngineAttackerEvidenceRow> DirectEvidence,
    [property: JsonPropertyName("chain_evidence")] IReadOnlyList<EngineAttackerEvidenceRow> ChainEvidence,
    [property: JsonPropertyName("identities")] IReadOnlyList<EngineIdentityRow> Identities,
    [property: JsonPropertyName("npc_corporation_ids")] IReadOnlyCollection<long> NpcCorporationIds);

public sealed record EnginePilotInputsResult(long CharacterId, EnginePilotInputs? Inputs, string? FailureReason);

public sealed record EngineGroupInputsResult(EngineGroupInputs? Inputs, string? FailureReason);

public static class EngineInputFailureReasons
{
    public const string Killmails = "repository_read_error:killmails";
    public const string Statistics = "repository_read_error:statistics";
    public const string Identity = "repository_read_error:identity";
    public const string ActivityCache = "repository_read_error:activity_cache";
    public const string DirectEvidence = "repository_read_error:direct_evidence";
    public const string ChainEvidence = "repository_read_error:chain_evidence";
    public const string NpcCorporations = "repository_read_error:npc_corporations";
}

using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Killright.Core.Style;
using Killright.Storage.Diagnostics;

namespace Killright.UI.Analysis;

public sealed class RustRecentStyleClient
{
    private readonly IKillrightEngineRuntime _runtime;

    public RustRecentStyleClient(IKillrightEngineRuntime runtime)
    {
        _runtime = runtime;
    }

    public async Task<PilotEngineAnalysisResult> AnalyzeAsync(
        long characterId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var request = new PilotAnalysisRequest(characterId, null);
            var requestJson = JsonSerializer.Serialize(request);
            var responseJson = await _runtime.AnalyzePilotAsync(requestJson, cancellationToken);
            var response = JsonSerializer.Deserialize<PilotAnalysisResponse>(responseJson);

            if (!string.IsNullOrWhiteSpace(response?.failure))
            {
                EngineFailureLog.Record($"engine analysis failed for character {characterId}: {response.failure}");
                return PilotEngineAnalysisResult.Failed(response.failure);
            }

            return new PilotEngineAnalysisResult(
                MapRecentStyle(response?.recent_style),
                MapThreatBand(response?.threat?.band),
                FailureReason: null);
        }
        catch (Exception exception)
        {
            EngineFailureLog.Record($"engine analysis threw for character {characterId}: {exception.Message}");
            return PilotEngineAnalysisResult.Failed("exception");
        }
    }

    public async Task<PilotGroupDetectionResult> AnalyzeGroupAsync(
        IReadOnlyList<long> scannedCharacterIds,
        CancellationToken cancellationToken = default)
    {
        if (scannedCharacterIds.Count == 0)
            return PilotGroupDetectionResult.Empty;

        try
        {
            var request = new PilotAnalysisRequest(scannedCharacterIds[0], scannedCharacterIds);
            var requestJson = JsonSerializer.Serialize(request);
            var responseJson = await _runtime.AnalyzePilotAsync(requestJson, cancellationToken);
            var response = JsonSerializer.Deserialize<PilotAnalysisResponse>(responseJson);

            if (!string.IsNullOrWhiteSpace(response?.failure) || response?.group_detection is null)
            {
                if (!string.IsNullOrWhiteSpace(response?.failure))
                    EngineFailureLog.Record($"engine group detection failed: {response.failure}");

                return PilotGroupDetectionResult.Empty;
            }

            var relationships = response.group_detection.relationships
                .Select(relationship => new PilotRelationship(
                    relationship.pilot_a_character_id,
                    relationship.pilot_b_character_id,
                    relationship.link_type == "Chain" ? RelationshipLinkType.Chain : RelationshipLinkType.Direct,
                    relationship.strength,
                    relationship.confidence,
                    relationship.total_shared_kills,
                    relationship.last_shared_kill_time_utc,
                    relationship.intermediaries_in_scan))
                .ToList();

            return new PilotGroupDetectionResult(relationships);
        }
        catch (Exception exception)
        {
            EngineFailureLog.Record($"engine group detection threw: {exception.Message}");
            return PilotGroupDetectionResult.Empty;
        }
    }

    private static StyleClassification MapRecentStyle(string? value)
    {
        var normalized = value?.Trim();
        return normalized switch
        {
            RecentStyleContract.Unknown => StyleClassification.Unknown,
            RecentStyleContract.Inactive => StyleClassification.Inactive,
            RecentStyleContract.Victim => StyleClassification.Victim,
            RecentStyleContract.Solo => StyleClassification.Solo,
            RecentStyleContract.Gang => StyleClassification.Gang,
            RecentStyleContract.Blob => StyleClassification.Blob,
            RecentStyleContract.Fleet => StyleClassification.Fleet,
            RecentStyleContract.Miner => StyleClassification.Miner,
            RecentStyleContract.Explorer => StyleClassification.Explorer,
            RecentStyleContract.Hauler => StyleClassification.Hauler,
            RecentStyleContract.PI => StyleClassification.PI,
            _ => StyleClassification.Unknown
        };
    }

    private static string MapThreatBand(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized)
            ? "Unk"
            : normalized == "Unknown"
                ? "Unk"
                : normalized;
    }

    private static class RecentStyleContract
    {
        public const string Unknown = "Unknown";
        public const string Inactive = "Inactive";
        public const string Victim = "Victim";
        public const string Solo = "Solo";
        public const string Gang = "Gang";
        public const string Blob = "Blob";
        public const string Fleet = "Fleet";
        public const string Miner = "Miner";
        public const string Explorer = "Explorer";
        public const string Hauler = "Hauler";
        public const string PI = "PI";
    }

    private sealed record PilotAnalysisRequest(long character_id, IReadOnlyList<long>? scanned_character_ids);

    private sealed class PilotAnalysisResponse
    {
        public long character_id { get; set; }
        public string? recent_style { get; set; }
        public ThreatAnalysisResponse? threat { get; set; }
        public GroupDetectionResponse? group_detection { get; set; }
        public string? failure { get; set; }
    }

    private sealed class ThreatAnalysisResponse
    {
        public int score { get; set; }
        public string? band { get; set; }
        public string? confidence { get; set; }
    }

    private sealed class GroupDetectionResponse
    {
        public List<GroupRelationshipResponse> relationships { get; set; } = new();
    }

    private sealed class GroupRelationshipResponse
    {
        public long pilot_a_character_id { get; set; }
        public long pilot_b_character_id { get; set; }
        public string link_type { get; set; } = string.Empty;
        public int strength { get; set; }
        public int confidence { get; set; }
        public long? total_shared_kills { get; set; }
        public string? last_shared_kill_time_utc { get; set; }
        public List<long>? intermediaries_in_scan { get; set; }
    }
}

public enum RelationshipLinkType
{
    Direct,
    Chain
}

public sealed record PilotRelationship(
    long PilotACharacterId,
    long PilotBCharacterId,
    RelationshipLinkType LinkType,
    int Strength,
    int Confidence,
    long? TotalSharedKills,
    string? LastSharedKillTimeUtc,
    IReadOnlyList<long>? IntermediariesInScan);

public sealed record PilotGroupDetectionResult(IReadOnlyList<PilotRelationship> Relationships)
{
    public static readonly PilotGroupDetectionResult Empty = new(Array.Empty<PilotRelationship>());

    public IEnumerable<PilotRelationship> ForCharacter(long characterId) =>
        Relationships.Where(relationship =>
            relationship.PilotACharacterId == characterId || relationship.PilotBCharacterId == characterId);
}

public sealed record PilotEngineAnalysisResult(
    StyleClassification RecentStyle,
    string ThreatBand,
    string? FailureReason)
{
    public static PilotEngineAnalysisResult Failed(string reason) => new(
        StyleClassification.Unknown,
        "Unk",
        reason);
}
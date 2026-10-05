using System.Collections.Generic;
using System.Linq;
using System.Globalization;
using System.Text.Json;
using Killright.Core.Style;
using Killright.Integration.zKill;
using Killright.Shared.Time;
using Killright.Shared.zKill;
using Killright.Storage.Diagnostics;
using Killright.UI.Configuration;

namespace Killright.UI.Analysis;

public sealed class RustRecentStyleClient
{
    private readonly IKillrightEngineRuntime _runtime;
    private readonly IReadOnlyList<ThreatBandSetting> _threatBands;

    public RustRecentStyleClient(IKillrightEngineRuntime runtime, IReadOnlyList<ThreatBandSetting>? threatBands = null)
    {
        _runtime = runtime;
        _threatBands = ThreatBandSetting.ValidateOrDefault(threatBands);
    }

    public async Task<PilotEngineAnalysisResult> AnalyzeAsync(
        long characterId,
        CancellationToken cancellationToken = default,
        ScanTimings? timings = null)
    {
        try
        {
            string requestJson;

            using (timings.Measure(ScanTimings.EngineLevel, "json_serialize", characterId))
            {
                var request = new PilotAnalysisRequest(characterId, null);
                requestJson = JsonSerializer.Serialize(request);
            }

            var responseJson = await _runtime.AnalyzePilotAsync(requestJson, cancellationToken, timings, characterId);
            PilotAnalysisResponse? response;

            using (timings.Measure(ScanTimings.EngineLevel, "json_deserialize", characterId))
            {
                response = JsonSerializer.Deserialize<PilotAnalysisResponse>(responseJson);
            }

            var engineTimings = MapTimings(response?.timings_ms, response?.timing_counts);
            timings.AddEngine(characterId, engineTimings?.TimingsMs, engineTimings?.TimingCounts);

            if (!string.IsNullOrWhiteSpace(response?.failure))
            {
                EngineFailureLog.Record($"engine analysis failed for character {characterId}: {response.failure}");
                return PilotEngineAnalysisResult.Failed(response.failure);
            }

            return new PilotEngineAnalysisResult(
                MapRecentStyle(response?.recent_style),
                response?.is_recent_podder ?? false,
                MapThreatScore(response?.threat?.score),
                FailureReason: null,
                Timings: engineTimings);
        }
        catch (Exception exception)
        {
            EngineFailureLog.Record($"engine analysis threw for character {characterId}: {exception.Message}");
            return PilotEngineAnalysisResult.Failed("exception");
        }
    }

    public async Task<IReadOnlyList<PilotEngineAnalysisResult>> AnalyzePilotsAsync(
        IReadOnlyList<long> characterIds,
        CancellationToken cancellationToken = default,
        ScanTimings? timings = null)
    {
        if (characterIds.Count == 0)
            return Array.Empty<PilotEngineAnalysisResult>();

        try
        {
            string requestJson;

            using (timings.Measure(ScanTimings.EngineLevel, "json_serialize"))
            {
                requestJson = JsonSerializer.Serialize(new PilotsAnalysisRequest(characterIds));
            }

            var responseJson = await _runtime.AnalyzePilotsAsync(requestJson, cancellationToken, timings);
            PilotsAnalysisResponse? response;

            using (timings.Measure(ScanTimings.EngineLevel, "json_deserialize"))
            {
                response = JsonSerializer.Deserialize<PilotsAnalysisResponse>(responseJson);
            }

            var engineTimings = MapTimings(response?.timings_ms, response?.timing_counts);
            timings.AddEngine(null, engineTimings?.TimingsMs, engineTimings?.TimingCounts);

            if (response is null || !string.IsNullOrWhiteSpace(response.failure))
            {
                var reason = response?.failure ?? "unreadable_response";
                EngineFailureLog.Record($"engine batch analysis failed: {reason}");

                return characterIds.Select(_ => PilotEngineAnalysisResult.Failed(reason)).ToList();
            }

            var byCharacter = new Dictionary<long, PilotAnalysisResponse>();

            foreach (var result in response.results)
                byCharacter.TryAdd(result.character_id, result);

            var now = ApplicationClock.UtcNow;

            using (timings.Measure(ScanTimings.EngineLevel, "result_map"))
            {
                return characterIds.Select(characterId =>
                {
                    if (!byCharacter.TryGetValue(characterId, out var result))
                        return PilotEngineAnalysisResult.Failed("missing_result");

                    if (!string.IsNullOrWhiteSpace(result.failure))
                    {
                        EngineFailureLog.Record($"engine analysis failed for character {characterId}: {result.failure}");
                        return PilotEngineAnalysisResult.Failed(result.failure);
                    }

                    return new PilotEngineAnalysisResult(
                        MapRecentStyle(result.recent_style),
                        result.is_recent_podder ?? false,
                        MapThreatScore(result.threat?.score),
                        FailureReason: null,
                        DerivedActivity: MapDerivedActivity(result.derived_activity));
                }).ToList();
            }
        }
        catch (Exception exception)
        {
            EngineFailureLog.Record($"engine batch analysis threw: {exception.Message}");

            return characterIds.Select(_ => PilotEngineAnalysisResult.Failed("exception")).ToList();
        }
    }

    public async Task<PilotGroupDetectionResult> AnalyzeGroupAsync(
        IReadOnlyList<long> scannedCharacterIds,
        CancellationToken cancellationToken = default,
        ScanTimings? timings = null)
    {
        if (scannedCharacterIds.Count == 0)
            return PilotGroupDetectionResult.Empty;

        try
        {
            string requestJson;

            using (timings.Measure(ScanTimings.EngineLevel, "json_serialize"))
            {
                var request = new PilotAnalysisRequest(scannedCharacterIds[0], scannedCharacterIds);
                requestJson = JsonSerializer.Serialize(request);
            }

            var responseJson = await _runtime.AnalyzePilotAsync(requestJson, cancellationToken, timings);
            PilotAnalysisResponse? response;

            using (timings.Measure(ScanTimings.EngineLevel, "json_deserialize"))
            {
                response = JsonSerializer.Deserialize<PilotAnalysisResponse>(responseJson);
            }

            var engineTimings = MapTimings(response?.timings_ms, response?.timing_counts);
            timings.AddEngine(null, engineTimings?.TimingsMs, engineTimings?.TimingCounts);

            if (!string.IsNullOrWhiteSpace(response?.failure) || response?.group_detection is null)
            {
                if (!string.IsNullOrWhiteSpace(response?.failure))
                    EngineFailureLog.Record($"engine group detection failed: {response.failure}");

                return PilotGroupDetectionResult.Empty;
            }

            List<PilotRelationship> relationships;

            using (timings.Measure(ScanTimings.EngineLevel, "relationship_map"))
            {
                relationships = response.group_detection.relationships
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
            }

            return new PilotGroupDetectionResult(relationships, engineTimings);
        }
        catch (Exception exception)
        {
            EngineFailureLog.Record($"engine group detection threw: {exception.Message}");
            return PilotGroupDetectionResult.Empty;
        }
    }

    private static EngineTimings? MapTimings(Dictionary<string, double>? timingsMs, Dictionary<string, long>? timingCounts)
    {
        if (timingsMs is null && timingCounts is null)
            return null;

        return new EngineTimings(
            timingsMs ?? new Dictionary<string, double>(),
            timingCounts ?? new Dictionary<string, long>());
    }

    private static PilotDerivedActivity? MapDerivedActivity(DerivedActivityResponse? response)
    {
        if (response is null)
            return null;

        return new PilotDerivedActivity(
            response.has_public_activity_data,
            response.kills_week,
            response.solo_week,
            ParseUtc(response.newest_non_pod_killmail?.kill_time_utc),
            response.newest_non_pod_killmail?.activity_type switch
            {
                "Kill" => zKillActivityType.Kill,
                "Loss" => zKillActivityType.Loss,
                _ => null
            },
            ParseUtc(response.newest_non_pod_kill_time_utc),
            response.info_week_losses);
    }

    private static DateTimeOffset? ParseUtc(string? value)
    {
        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed
            : null;
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
            _ => StyleClassification.Unknown
        };
    }

    private string MapThreatScore(int? score)
    {
        return ThreatBandMapper.MapScore(score, _threatBands);
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
    }

    private sealed record PilotAnalysisRequest(long character_id, IReadOnlyList<long>? scanned_character_ids);

    private sealed record PilotsAnalysisRequest(IReadOnlyList<long> character_ids);

    private sealed class PilotsAnalysisResponse
    {
        public List<PilotAnalysisResponse> results { get; set; } = new();
        public string? failure { get; set; }
        public Dictionary<string, double>? timings_ms { get; set; }
        public Dictionary<string, long>? timing_counts { get; set; }
    }

    private sealed class DerivedActivityResponse
    {
        public bool has_public_activity_data { get; set; }
        public int? kills_week { get; set; }
        public int? solo_week { get; set; }
        public int? info_week_losses { get; set; }
        public NewestKillmailResponse? newest_non_pod_killmail { get; set; }
        public string? newest_non_pod_kill_time_utc { get; set; }
    }

    private sealed class NewestKillmailResponse
    {
        public string? kill_time_utc { get; set; }
        public string? activity_type { get; set; }
    }

    private sealed class PilotAnalysisResponse
    {
        public long character_id { get; set; }
        public string? recent_style { get; set; }
        public bool? is_recent_podder { get; set; }
        public ThreatAnalysisResponse? threat { get; set; }
        public GroupDetectionResponse? group_detection { get; set; }
        public DerivedActivityResponse? derived_activity { get; set; }
        public string? failure { get; set; }
        public Dictionary<string, double>? timings_ms { get; set; }
        public Dictionary<string, long>? timing_counts { get; set; }
    }

    private sealed class ThreatAnalysisResponse
    {
        public int score { get; set; }
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

public sealed record EngineTimings(
    IReadOnlyDictionary<string, double> TimingsMs,
    IReadOnlyDictionary<string, long> TimingCounts);

public sealed record PilotGroupDetectionResult(
    IReadOnlyList<PilotRelationship> Relationships,
    EngineTimings? Timings = null)
{
    public static readonly PilotGroupDetectionResult Empty = new(Array.Empty<PilotRelationship>());

    public IEnumerable<PilotRelationship> ForCharacter(long characterId) =>
        Relationships.Where(relationship =>
            relationship.PilotACharacterId == characterId || relationship.PilotBCharacterId == characterId);
}

public sealed record PilotDerivedActivity(
    bool HasPublicActivityData,
    int? KillsWeek,
    int? SoloWeek,
    DateTimeOffset? NewestKillTimeUtc,
    zKillActivityType? NewestKillActivityType,
    DateTimeOffset? LastKillUtc,
    int? InfoWeekLosses = null)
{
    public zKillActivity ToActivity(long characterId, DateTimeOffset checkedAtUtc) => new(
        characterId,
        HasPublicActivityData,
        KillsWeek,
        SoloWeek,
        NewestKillTimeUtc,
        NewestKillActivityType,
        checkedAtUtc,
        LastKillUtc: LastKillUtc);
}

public sealed record PilotEngineAnalysisResult(
    StyleClassification RecentStyle,
    bool IsRecentPodder,
    string ThreatBand,
    string? FailureReason,
    EngineTimings? Timings = null,
    PilotDerivedActivity? DerivedActivity = null)
{
    public static PilotEngineAnalysisResult Failed(string reason) => new(
        StyleClassification.Unknown,
        false,
        ThreatBandMapper.Unknown,
        reason);
}

public static class ThreatBandMapper
{
    public const string None = "None";
    public const string Unknown = "Unk";

    public static string MapScore(int? score, IReadOnlyList<ThreatBandSetting> bands)
    {
        if (score is not int value)
            return Unknown;

        if (value <= 0)
            return None;

        foreach (var band in bands)
        {
            if (value >= band.MinimumScore && value <= band.MaximumScore)
                return band.Name;
        }

        return None;
    }
}
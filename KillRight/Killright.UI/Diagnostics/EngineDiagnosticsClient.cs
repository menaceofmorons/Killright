using System.Data;
using System.Text.Json;
using Killright.UI.Analysis;

namespace Killright.UI.Diagnostics;

public sealed class EngineDiagnosticsClient
{
    private readonly IKillrightEngineRuntime _runtime;

    public EngineDiagnosticsClient(IKillrightEngineRuntime runtime)
    {
        _runtime = runtime;
    }

    public async Task<GroupDetectionDiagnosticsResult> DiagnoseGroupDetectionAsync(
        IReadOnlyList<long> scannedCharacterIds,
        CancellationToken cancellationToken = default)
    {
        if (scannedCharacterIds.Count == 0)
            return GroupDetectionDiagnosticsResult.Empty;

        var request = new DiagnosticsRequest(scannedCharacterIds[0], scannedCharacterIds);
        var requestJson = JsonSerializer.Serialize(request);
        var responseJson = await _runtime.DiagnoseGroupDetectionAsync(requestJson, cancellationToken);
        var envelope = JsonSerializer.Deserialize<GroupDetectionDiagnosticsEnvelope>(responseJson);

        if (!string.IsNullOrWhiteSpace(envelope?.failure) || envelope?.diagnostics is null)
            return GroupDetectionDiagnosticsResult.Empty;

        return new GroupDetectionDiagnosticsResult(
            BuildDirectRelationshipsTable(envelope.diagnostics.direct_relationships),
            BuildChainsTable(envelope.diagnostics.chains),
            BuildHubsTable(envelope.diagnostics.hubs),
            envelope.diagnostics.direct_analysis_duration_ms,
            envelope.diagnostics.chain_analysis_duration_ms);
    }

    public async Task<DataTable> DiagnoseThreatAsync(
        long characterId,
        CancellationToken cancellationToken = default)
    {
        var request = new DiagnosticsRequest(characterId, null);
        var requestJson = JsonSerializer.Serialize(request);
        var responseJson = await _runtime.DiagnoseThreatAsync(requestJson, cancellationToken);
        var envelope = JsonSerializer.Deserialize<ThreatDiagnosticsEnvelope>(responseJson);

        var table = new DataTable();
        table.Columns.Add("Component", typeof(string));
        table.Columns.Add("Value", typeof(string));

        if (!string.IsNullOrWhiteSpace(envelope?.failure) || envelope?.diagnostics is null)
        {
            table.Rows.Add("Failure", envelope?.failure ?? "no_response");
            return table;
        }

        var diagnostics = envelope.diagnostics;
        table.Rows.Add("Historical Capability", diagnostics.historical_capability.ToString());
        table.Rows.Add("Survivability", diagnostics.survivability.ToString());
        table.Rows.Add("Loss Quality", diagnostics.loss_quality.ToString());
        table.Rows.Add("Recent Activity Modifier", diagnostics.recent_activity_modifier.ToString("0.###"));
        table.Rows.Add("Security Modifier", diagnostics.security_modifier.ToString());
        table.Rows.Add("Score", diagnostics.score.ToString());
        table.Rows.Add("Confidence", diagnostics.confidence.ToString());
        table.Rows.Add("Coverage Start Present", diagnostics.coverage_start_present.ToString());
        table.Rows.Add("Observed Days", diagnostics.observed_days.ToString("0.###"));
        table.Rows.Add("Counted Kills", diagnostics.counted_kills.ToString());
        table.Rows.Add("Daily Rate", diagnostics.daily_rate.ToString("0.###"));

        return table;
    }

    private static DataTable BuildDirectRelationshipsTable(List<DirectRelationshipDiagnosticPayload> rows)
    {
        var table = new DataTable();
        table.Columns.Add("PilotA", typeof(long));
        table.Columns.Add("PilotB", typeof(long));
        table.Columns.Add("Qualifies", typeof(bool));
        table.Columns.Add("GatedByCurrentMembership", typeof(bool));
        table.Columns.Add("AfterSplitBranch", typeof(string));
        table.Columns.Add("CountedSharedKills", typeof(long));
        table.Columns.Add("SplitBonusApplied", typeof(bool));
        table.Columns.Add("GangQuality", typeof(string));
        table.Columns.Add("SampleFactor", typeof(string));
        table.Columns.Add("Strength", typeof(string));
        table.Columns.Add("Confidence", typeof(string));
        table.Columns.Add("LastCountedKillTimeUtc", typeof(string));
        table.Columns.Add("ContributingKillmails", typeof(string));

        foreach (var row in rows)
        {
            table.Rows.Add(
                row.pilot_a_character_id,
                row.pilot_b_character_id,
                row.qualifies,
                row.gated_by_current_membership,
                row.after_split_branch,
                row.counted_shared_kills,
                row.split_bonus_applied,
                row.gang_quality?.ToString("0.##") ?? string.Empty,
                row.sample_factor?.ToString("0.##") ?? string.Empty,
                row.strength?.ToString() ?? string.Empty,
                row.confidence?.ToString() ?? string.Empty,
                row.last_counted_kill_time_utc ?? string.Empty,
                string.Join("; ", row.contributing_killmails.Select(killmail =>
                    $"{killmail.killmail_id}@{killmail.kill_time_utc}" +
                    $"({(killmail.is_same_corporation_or_alliance ? "same" : "not-same")}," +
                    $" gang {killmail.unique_attacker_count})")));
        }

        return table;
    }

    private static DataTable BuildChainsTable(List<ChainedRelationshipDiagnosticPayload> rows)
    {
        var table = new DataTable();
        table.Columns.Add("PilotA", typeof(long));
        table.Columns.Add("PilotC", typeof(long));
        table.Columns.Add("IntermediaryPilot", typeof(long));
        table.Columns.Add("IntermediaryInScan", typeof(bool));
        table.Columns.Add("LinkAbCountedSharedKills", typeof(long));
        table.Columns.Add("LinkAbStrength", typeof(int));
        table.Columns.Add("LinkAbConfidence", typeof(int));
        table.Columns.Add("LinkCbCountedSharedKills", typeof(long));
        table.Columns.Add("LinkCbStrength", typeof(int));
        table.Columns.Add("LinkCbConfidence", typeof(int));
        table.Columns.Add("ChainAgeDays", typeof(string));
        table.Columns.Add("ChainStrengthForThisIntermediary", typeof(int));
        table.Columns.Add("IsStrongestIntermediaryForPair", typeof(bool));
        table.Columns.Add("PairDistinctIntermediaryCount", typeof(long));
        table.Columns.Add("PairIntermediaryBonus", typeof(int));
        table.Columns.Add("PairChainConfidence", typeof(int));

        foreach (var row in rows)
        {
            table.Rows.Add(
                row.pilot_a_character_id,
                row.pilot_c_character_id,
                row.intermediary_pilot_character_id,
                row.intermediary_in_scan,
                row.link_ab_counted_shared_kills,
                row.link_ab_strength,
                row.link_ab_confidence,
                row.link_cb_counted_shared_kills,
                row.link_cb_strength,
                row.link_cb_confidence,
                row.chain_age_days.ToString("0.##"),
                row.chain_strength_for_this_intermediary,
                row.is_strongest_intermediary_for_pair,
                row.pair_distinct_intermediary_count,
                row.pair_intermediary_bonus,
                row.pair_chain_confidence);
        }

        return table;
    }

    private static DataTable BuildHubsTable(List<IntermediaryHubSummaryPayload> rows)
    {
        var table = new DataTable();
        table.Columns.Add("IntermediaryPilot", typeof(long));
        table.Columns.Add("IntermediaryInScan", typeof(bool));
        table.Columns.Add("ScannedPilotsLinkedCount", typeof(long));

        foreach (var row in rows)
        {
            table.Rows.Add(row.intermediary_pilot_character_id, row.intermediary_in_scan, row.scanned_pilots_linked_count);
        }

        return table;
    }

    private sealed record DiagnosticsRequest(long character_id, IReadOnlyList<long>? scanned_character_ids);

    private sealed class GroupDetectionDiagnosticsEnvelope
    {
        public long character_id { get; set; }
        public GroupDetectionDiagnosticsPayload? diagnostics { get; set; }
        public string? failure { get; set; }
    }

    private sealed class GroupDetectionDiagnosticsPayload
    {
        public List<DirectRelationshipDiagnosticPayload> direct_relationships { get; set; } = new();
        public List<ChainedRelationshipDiagnosticPayload> chains { get; set; } = new();
        public List<IntermediaryHubSummaryPayload> hubs { get; set; } = new();
        public long direct_analysis_duration_ms { get; set; }
        public long chain_analysis_duration_ms { get; set; }
    }

    private sealed class ContributingKillmailPayload
    {
        public long killmail_id { get; set; }
        public string kill_time_utc { get; set; } = string.Empty;
        public bool is_same_corporation_or_alliance { get; set; }
        public long unique_attacker_count { get; set; }
    }

    private sealed class DirectRelationshipDiagnosticPayload
    {
        public long pilot_a_character_id { get; set; }
        public long pilot_b_character_id { get; set; }
        public bool gated_by_current_membership { get; set; }
        public string after_split_branch { get; set; } = string.Empty;
        public List<ContributingKillmailPayload> contributing_killmails { get; set; } = new();
        public long counted_shared_kills { get; set; }
        public bool split_bonus_applied { get; set; }
        public bool qualifies { get; set; }
        public string? last_counted_kill_time_utc { get; set; }
        public double? gang_quality { get; set; }
        public double? sample_factor { get; set; }
        public int? strength { get; set; }
        public int? confidence { get; set; }
    }

    private sealed class ChainedRelationshipDiagnosticPayload
    {
        public long pilot_a_character_id { get; set; }
        public long pilot_c_character_id { get; set; }
        public long intermediary_pilot_character_id { get; set; }
        public bool intermediary_in_scan { get; set; }
        public long link_ab_counted_shared_kills { get; set; }
        public string link_ab_most_recent_in_window_kill_time_utc { get; set; } = string.Empty;
        public bool link_ab_split_bonus_applied { get; set; }
        public int link_ab_strength { get; set; }
        public int link_ab_confidence { get; set; }
        public long link_cb_counted_shared_kills { get; set; }
        public string link_cb_most_recent_in_window_kill_time_utc { get; set; } = string.Empty;
        public bool link_cb_split_bonus_applied { get; set; }
        public int link_cb_strength { get; set; }
        public int link_cb_confidence { get; set; }
        public double chain_age_days { get; set; }
        public int chain_strength_for_this_intermediary { get; set; }
        public bool is_strongest_intermediary_for_pair { get; set; }
        public long pair_distinct_intermediary_count { get; set; }
        public int pair_intermediary_bonus { get; set; }
        public int pair_chain_confidence { get; set; }
    }

    private sealed class IntermediaryHubSummaryPayload
    {
        public long intermediary_pilot_character_id { get; set; }
        public bool intermediary_in_scan { get; set; }
        public long scanned_pilots_linked_count { get; set; }
    }

    private sealed class ThreatDiagnosticsEnvelope
    {
        public long character_id { get; set; }
        public ThreatDiagnosticsPayload? diagnostics { get; set; }
        public string? failure { get; set; }
    }

    private sealed class ThreatDiagnosticsPayload
    {
        public int historical_capability { get; set; }
        public int survivability { get; set; }
        public int loss_quality { get; set; }
        public double recent_activity_modifier { get; set; }
        public int security_modifier { get; set; }
        public int score { get; set; }
        public int confidence { get; set; }
        public bool coverage_start_present { get; set; }
        public double observed_days { get; set; }
        public long counted_kills { get; set; }
        public double daily_rate { get; set; }
    }
}

public sealed record GroupDetectionDiagnosticsResult(
    DataTable DirectRelationships,
    DataTable Chains,
    DataTable Hubs,
    long DirectAnalysisDurationMs,
    long ChainAnalysisDurationMs)
{
    public static GroupDetectionDiagnosticsResult Empty { get; } = new(
        new DataTable(), new DataTable(), new DataTable(), 0, 0);
}

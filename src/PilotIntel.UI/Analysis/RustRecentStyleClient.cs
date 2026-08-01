using System.Text.Json;
using PilotIntel.Core.Style;

namespace PilotIntel.UI.Analysis;

public sealed class RustRecentStyleClient
{
    private readonly IPIntelEngineRuntime _runtime;

    public RustRecentStyleClient(IPIntelEngineRuntime runtime)
    {
        _runtime = runtime;
    }

    public async Task<PilotEngineAnalysisResult> AnalyzeAsync(
        long characterId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var request = new PilotAnalysisRequest(characterId);
            var requestJson = JsonSerializer.Serialize(request);
            var responseJson = await _runtime.AnalyzePilotAsync(requestJson, cancellationToken);
            var response = JsonSerializer.Deserialize<PilotAnalysisResponse>(responseJson);
            return new PilotEngineAnalysisResult(
                MapRecentStyle(response?.recent_style),
                MapThreatBand(response?.threat?.band));
        }
        catch
        {
            return PilotEngineAnalysisResult.Unknown;
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

    private sealed record PilotAnalysisRequest(long character_id);

    private sealed class PilotAnalysisResponse
    {
        public long character_id { get; set; }
        public string? recent_style { get; set; }
        public ThreatAnalysisResponse? threat { get; set; }
    }

    private sealed class ThreatAnalysisResponse
    {
        public int score { get; set; }
        public string? band { get; set; }
        public string? confidence { get; set; }
    }
}

public sealed record PilotEngineAnalysisResult(
    StyleClassification RecentStyle,
    string ThreatBand)
{
    public static PilotEngineAnalysisResult Unknown { get; } = new(
        StyleClassification.Unknown,
        "Unk");
}
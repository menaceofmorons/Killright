using System.Net.Http.Headers;
using System.Text.Json;
using Killright.Shared.Killmails;
using Killright.Shared.Time;
using Killright.Shared.zKill;

namespace Killright.Integration.zKill;

public sealed class zKillClient : IzKillClient
{
    private readonly HttpClient _http;
    private readonly zKillClientOptions _options;

    public zKillClient(HttpClient http, zKillClientOptions? options = null)
    {
        _http = http;
        _options = options ?? new zKillClientOptions();
        _http.BaseAddress ??= _options.BaseUri;

        if (!_http.DefaultRequestHeaders.UserAgent.Any())
            _http.DefaultRequestHeaders.UserAgent.ParseAdd(_options.UserAgent);

        if (!_http.DefaultRequestHeaders.AcceptEncoding.Any(x => x.Value.Equals("gzip", StringComparison.OrdinalIgnoreCase)))
            _http.DefaultRequestHeaders.AcceptEncoding.Add(new StringWithQualityHeaderValue("gzip"));
    }

    public async Task<zKillRecentKillmailResult> GetRecentKillmailsAsync(
        long characterId,
        int pastSeconds,
        CancellationToken cancellationToken = default)
    {
        var safePastSeconds = Math.Max(1, pastSeconds);

        try
        {
            using var response = await _http.GetAsync(
                $"api/characterID/{characterId}/pastSeconds/{safePastSeconds}/",
                cancellationToken);

            if ((int)response.StatusCode == 204)
                return new zKillRecentKillmailResult(zKillRecentKillmailOutcome.Success, [], []);

            if (!response.IsSuccessStatusCode)
                return new zKillRecentKillmailResult(zKillRecentKillmailOutcome.Failure, [], []);

            var json = await response.Content.ReadAsStringAsync(cancellationToken);

            if (IsNoHistoryResponse(json))
                return new zKillRecentKillmailResult(zKillRecentKillmailOutcome.NoHistory, [], []);

            var payload = JsonSerializer.Deserialize<List<zKillRecentKillmailDto>>(json) ?? [];
            var records = BuildKillmailRecords(payload, characterId);
            var rawKillmails = BuildRawKillmails(payload);

            return new zKillRecentKillmailResult(zKillRecentKillmailOutcome.Success, records, rawKillmails);
        }
        catch
        {
            return new zKillRecentKillmailResult(zKillRecentKillmailOutcome.Failure, [], []);
        }
    }

    public async Task<zKillStatisticsResult> GetStatisticsAsync(
        long characterId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await _http.GetAsync(
                $"api/stats/characterID/{characterId}/kills/",
                cancellationToken);

            if (!response.IsSuccessStatusCode)
                return new zKillStatisticsResult(zKillStatisticsOutcome.Failure, null);

            var json = await response.Content.ReadAsStringAsync(cancellationToken);

            if (IsNoHistoryResponse(json))
                return new zKillStatisticsResult(zKillStatisticsOutcome.NoHistory, null);

            var statistics = JsonSerializer.Deserialize<zKillStatistics>(json);

            return statistics is null
                ? new zKillStatisticsResult(zKillStatisticsOutcome.Failure, null)
                : new zKillStatisticsResult(zKillStatisticsOutcome.Success, statistics);
        }
        catch
        {
            return new zKillStatisticsResult(zKillStatisticsOutcome.Failure, null);
        }
    }

    private static bool IsNoHistoryResponse(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);

            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("error", out var errorProperty)
                && errorProperty.ValueKind == JsonValueKind.String
                && errorProperty.GetString() == "Invalid type or id";
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static IReadOnlyList<KillmailRecord> BuildKillmailRecords(
        List<zKillRecentKillmailDto> payload,
        long characterId)
    {
        var records = new List<KillmailRecord>();
        var cachedAtUtc = ApplicationClock.UtcNow;

        foreach (var killmail in payload)
        {
            var isLoss = killmail.victim.character_id == characterId;
            var shipTypeId = isLoss
                ? killmail.victim.ship_type_id
                : killmail.attackers.FirstOrDefault(attacker => attacker.character_id == characterId)?.ship_type_id;

            records.Add(new KillmailRecord(
                killmail.killmail_id,
                killmail.zkb.hash,
                characterId,
                killmail.killmail_time,
                isLoss,
                killmail.attackers.Count,
                killmail.zkb.solo,
                shipTypeId,
                killmail.solar_system_id,
                killmail.zkb.locationID,
                killmail.zkb.npc,
                cachedAtUtc));
        }

        return records;
    }

    private static IReadOnlyList<RawKillmail> BuildRawKillmails(
        List<zKillRecentKillmailDto> payload)
    {
        var raw = new List<RawKillmail>();

        foreach (var killmail in payload)
        {
            var attackers = killmail.attackers
                .Select(attacker => new KillmailAttacker(
                    attacker.character_id,
                    attacker.corporation_id,
                    attacker.alliance_id,
                    attacker.ship_type_id))
                .ToList();

            raw.Add(new RawKillmail(
                killmail.killmail_id,
                killmail.zkb.hash,
                killmail.killmail_time,
                killmail.solar_system_id,
                killmail.zkb.locationID,
                killmail.victim.character_id,
                killmail.victim.ship_type_id,
                killmail.zkb.solo,
                killmail.zkb.npc,
                attackers));
        }

        return raw;
    }

    private sealed class zKillRecentKillmailDto
    {
        public long killmail_id { get; set; }
        public DateTimeOffset killmail_time { get; set; }
        public long solar_system_id { get; set; }
        public zKillVictimDto victim { get; set; } = new();
        public List<zKillAttackerDto> attackers { get; set; } = [];
        public zKillMetadataDto zkb { get; set; } = new();
    }

    private sealed class zKillVictimDto
    {
        public long? character_id { get; set; }
        public long? ship_type_id { get; set; }
    }

    private sealed class zKillAttackerDto
    {
        public long? character_id { get; set; }
        public long? corporation_id { get; set; }
        public long? alliance_id { get; set; }
        public long? ship_type_id { get; set; }
    }

    private sealed class zKillMetadataDto
    {
        public string? hash { get; set; }
        public long? locationID { get; set; }
        public bool solo { get; set; }
        public bool npc { get; set; }
    }
}

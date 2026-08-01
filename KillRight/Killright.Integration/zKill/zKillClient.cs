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

    public async Task<IReadOnlyList<KillmailRecord>> GetRecentKillmailsAsync(
        long characterId,
        int pastSeconds,
        CancellationToken cancellationToken = default)
    {
        var safePastSeconds = Math.Max(1, pastSeconds);

        return await LoadKillmailsAsync(
            $"api/characterID/{characterId}/pastSeconds/{safePastSeconds}/",
            characterId,
            cancellationToken);
    }

    public async Task<zKillActivity?> GetLatestActivityAsync(
        long characterId,
        CancellationToken cancellationToken = default)
    {
        var payload = await LoadzKillKillmailsAsync(
            $"api/characterID/{characterId}/",
            cancellationToken);

        var latest = payload.FirstOrDefault();

        if (latest is null)
            return null;

        var isLoss = latest.victim.character_id == characterId;

        return new zKillActivity(
            characterId,
            true,
            0,
            0,
            latest.killmail_time,
            isLoss ? zKillActivityType.Loss : zKillActivityType.Kill,
            ApplicationClock.UtcNow);
    }

    public async Task<zKillStatistics?> GetStatisticsAsync(
        long characterId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await _http.GetAsync(
                $"api/stats/characterID/{characterId}/kills/",
                cancellationToken);

            if (!response.IsSuccessStatusCode)
                return null;

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);

            return await JsonSerializer.DeserializeAsync<zKillStatistics>(
                stream,
                cancellationToken: cancellationToken);
        }
        catch
        {
            return null;
        }
    }

    private async Task<IReadOnlyList<KillmailRecord>> LoadKillmailsAsync(
        string requestUri,
        long characterId,
        CancellationToken cancellationToken)
    {
        var payload = await LoadzKillKillmailsAsync(requestUri, cancellationToken);
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

    private async Task<IReadOnlyList<zKillRecentKillmailDto>> LoadzKillKillmailsAsync(
        string requestUri,
        CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _http.GetAsync(requestUri, cancellationToken);

            if ((int)response.StatusCode == 204)
                return [];

            if (!response.IsSuccessStatusCode)
                return [];

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);

            return await JsonSerializer.DeserializeAsync<List<zKillRecentKillmailDto>>(
                stream,
                cancellationToken: cancellationToken) ?? [];
        }
        catch
        {
            return [];
        }
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
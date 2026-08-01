using System.Net.Http.Json;
using System.Text.Json;
using Killright.Core.Models;
using Killright.Shared;

namespace Killright.Integration.Esi;

public sealed class EsiClient : IEsiClient
{
    private readonly HttpClient _http;
    private readonly EsiClientOptions _options;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public EsiClient(HttpClient http, EsiClientOptions? options = null)
    {
        _http = http;
        _options = options ?? new EsiClientOptions();
        _http.BaseAddress ??= _options.BaseUri;
        if (!_http.DefaultRequestHeaders.UserAgent.Any())
        {
            _http.DefaultRequestHeaders.UserAgent.ParseAdd(_options.UserAgent);
        }
    }

    public async Task<IReadOnlyList<Pilot>> ResolvePilotsAsync(IEnumerable<string> exactPilotNames, CancellationToken cancellationToken = default)
    {
        var tasks = exactPilotNames
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => ResolvePilotAsync(x.Trim(), cancellationToken));

        return await Task.WhenAll(tasks);
    }

    public async Task<Pilot> ResolvePilotAsync(string exactPilotName, CancellationToken cancellationToken = default)
    {
        var character = await ResolveCharacterIdByExactNameAsync(exactPilotName, cancellationToken);
        if (character is null)
        {
            return new Pilot
            {
                InputName = exactPilotName,
                VerifyStatus = VerifyStatus.NoMatch
            };
        }

        var characterInfo = await GetCharacterAsync(character.Id, cancellationToken);
        if (characterInfo is null)
        {
            return new Pilot
            {
                InputName = exactPilotName,
                CharacterId = character.Id,
                CharacterName = character.Name,
                VerifyStatus = VerifyStatus.Partial
            };
        }

        var corp = await GetCorporationAsync(characterInfo.CorporationId, cancellationToken);
        Alliance? alliance = null;
        if (characterInfo.AllianceId.HasValue)
        {
            alliance = await GetAllianceAsync(characterInfo.AllianceId.Value, cancellationToken);
        }

        return new Pilot
        {
            InputName = exactPilotName,
            CharacterId = character.Id,
            CharacterName = characterInfo.Name,
            VerifyStatus = VerifyStatus.Partial,
            SecurityStatus = characterInfo.SecurityStatus,
            Corporation = corp,
            Alliance = alliance
        };
    }

    private async Task<EsiResolvedEntity?> ResolveCharacterIdByExactNameAsync(string exactPilotName, CancellationToken cancellationToken)
    {
        var requestUri = $"universe/ids/?datasource={_options.DataSource}&language={_options.Language}";
        var names = new[] { exactPilotName };
        using var response = await _http.PostAsJsonAsync(requestUri, names, JsonOptions, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var result = await response.Content.ReadFromJsonAsync<EsiUniverseIdsResponse>(JsonOptions, cancellationToken);
        var match = result?.Characters?.FirstOrDefault(c => string.Equals(c.Name, exactPilotName, StringComparison.Ordinal));
        return match;
    }

    private async Task<EsiCharacterResponse?> GetCharacterAsync(long characterId, CancellationToken cancellationToken)
    {
        var uri = $"characters/{characterId}/?datasource={_options.DataSource}";
        return await GetOrNullAsync<EsiCharacterResponse>(uri, cancellationToken);
    }

    private async Task<Corporation?> GetCorporationAsync(long corporationId, CancellationToken cancellationToken)
    {
        var uri = $"corporations/{corporationId}/?datasource={_options.DataSource}";
        var dto = await GetOrNullAsync<EsiCorporationResponse>(uri, cancellationToken);
        return dto is null ? null : new Corporation
        {
            CorporationId = corporationId,
            Name = dto.Name,
            Ticker = dto.Ticker
        };
    }

    private async Task<Alliance?> GetAllianceAsync(long allianceId, CancellationToken cancellationToken)
    {
        var uri = $"alliances/{allianceId}/?datasource={_options.DataSource}";
        var dto = await GetOrNullAsync<EsiAllianceResponse>(uri, cancellationToken);
        return dto is null ? null : new Alliance
        {
            AllianceId = allianceId,
            Name = dto.Name,
            Ticker = dto.Ticker
        };
    }

    private async Task<T?> GetOrNullAsync<T>(string uri, CancellationToken cancellationToken)
    {
        using var response = await _http.GetAsync(uri, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return default;
        }
        return await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken);
    }
}

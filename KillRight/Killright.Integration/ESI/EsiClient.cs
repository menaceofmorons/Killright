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
        var lookup = await ResolveIdByExactNameAsync(exactPilotName, EsiUniverseIdsCategory.Characters, cancellationToken);
        if (!lookup.Succeeded)
        {
            return new Pilot
            {
                InputName = exactPilotName,
                VerifyStatus = VerifyStatus.Failed
            };
        }

        if (lookup.Entity is null)
        {
            return new Pilot
            {
                InputName = exactPilotName,
                VerifyStatus = VerifyStatus.NoMatch
            };
        }

        var character = lookup.Entity;
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
            Alliance = alliance,
            AllianceId = characterInfo.AllianceId,
            Birthday = characterInfo.Birthday is null
                ? null
                : DateOnly.FromDateTime(characterInfo.Birthday.Value.UtcDateTime)
        };
    }

    public async Task<EsiResolvedIdentity?> ResolveEntityByExactNameAsync(string exactName, IgnoreEntryType type, CancellationToken cancellationToken = default)
    {
        var category = type switch
        {
            IgnoreEntryType.Corporation => EsiUniverseIdsCategory.Corporations,
            IgnoreEntryType.Alliance => EsiUniverseIdsCategory.Alliances,
            _ => EsiUniverseIdsCategory.Characters
        };

        var lookup = await ResolveIdByExactNameAsync(exactName, category, cancellationToken);

        if (!lookup.Succeeded || lookup.Entity is null)
            return null;

        return new EsiResolvedIdentity { Id = lookup.Entity.Id, Name = lookup.Entity.Name };
    }

    private static class EsiUniverseIdsCategory
    {
        public const string Characters = "characters";
        public const string Corporations = "corporations";
        public const string Alliances = "alliances";
    }

    private async Task<EntityLookupResult> ResolveIdByExactNameAsync(string exactName, string category, CancellationToken cancellationToken)
    {
        try
        {
            var requestUri = $"universe/ids/?datasource={_options.DataSource}&language={_options.Language}";
            var names = new[] { exactName };
            using var response = await _http.PostAsJsonAsync(requestUri, names, JsonOptions, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return new EntityLookupResult(false, null);
            }

            var result = await response.Content.ReadFromJsonAsync<EsiUniverseIdsResponse>(JsonOptions, cancellationToken);
            var match = category switch
            {
                EsiUniverseIdsCategory.Corporations => result?.Corporations?.FirstOrDefault(),
                EsiUniverseIdsCategory.Alliances => result?.Alliances?.FirstOrDefault(),
                _ => result?.Characters?.FirstOrDefault()
            };
            return new EntityLookupResult(true, match);
        }
        catch
        {
            return new EntityLookupResult(false, null);
        }
    }

    private readonly record struct EntityLookupResult(bool Succeeded, EsiResolvedEntity? Entity);

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
        try
        {
            using var response = await _http.GetAsync(uri, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return default;
            }
            return await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken);
        }
        catch
        {
            return default;
        }
    }
}

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
    private static readonly TimeSpan DefaultBackoff = TimeSpan.FromSeconds(60);

    public const int BatchChunkSize = 500;

    private readonly Func<DateTimeOffset> _utcNow;
    private long _requestCount;
    private long _backoffUntilTicks;

    private readonly Action<string>? _log;

    public EsiClient(
        HttpClient http,
        EsiClientOptions? options = null,
        Func<DateTimeOffset>? utcNow = null,
        Action<string>? log = null)
    {
        _http = http;
        _log = log;
        _options = options ?? new EsiClientOptions();
        _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
        _http.BaseAddress ??= _options.BaseUri;
        if (!_http.DefaultRequestHeaders.UserAgent.Any())
        {
            _http.DefaultRequestHeaders.UserAgent.ParseAdd(_options.UserAgent);
        }
    }

    public long RequestCount => Interlocked.Read(ref _requestCount);

    public async Task<IReadOnlyDictionary<string, EsiNameLookup>> ResolveNamesAsync(
        IReadOnlyList<string> exactNames,
        CancellationToken cancellationToken = default)
    {
        var results = new Dictionary<string, EsiNameLookup>(StringComparer.OrdinalIgnoreCase);
        var distinctNames = exactNames
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var requestUri = $"universe/ids/?datasource={_options.DataSource}&language={_options.Language}";

        foreach (var chunk in distinctNames.Chunk(BatchChunkSize))
        {
            var response = await PostJsonAsync<EsiUniverseIdsResponse>(requestUri, chunk, cancellationToken);

            if (response is null)
            {
                foreach (var name in chunk)
                    results[name] = EsiNameLookup.Failed;

                continue;
            }

            var matchesByName = new Dictionary<string, EsiResolvedEntity>(StringComparer.OrdinalIgnoreCase);

            foreach (var match in response.Characters ?? [])
                matchesByName.TryAdd(match.Name, match);

            foreach (var name in chunk)
            {
                results[name] = matchesByName.TryGetValue(name, out var match)
                    ? EsiNameLookup.Matched(match.Id, match.Name)
                    : EsiNameLookup.NoMatch;
            }
        }

        return results;
    }

    public async Task<IReadOnlyDictionary<long, EsiAffiliation>> GetAffiliationsAsync(
        IReadOnlyList<long> characterIds,
        CancellationToken cancellationToken = default)
    {
        var results = new Dictionary<long, EsiAffiliation>();
        var requestUri = $"characters/affiliation/?datasource={_options.DataSource}";

        foreach (var chunk in characterIds.Distinct().Chunk(BatchChunkSize))
        {
            var response = await PostJsonAsync<List<EsiAffiliationResponse>>(requestUri, chunk, cancellationToken);

            if (response is null)
                continue;

            foreach (var item in response)
                results[item.CharacterId] = new EsiAffiliation(item.CharacterId, item.CorporationId, item.AllianceId);
        }

        return results;
    }

    public async Task<IReadOnlyDictionary<long, string>> GetEntityNamesAsync(
        IReadOnlyList<long> entityIds,
        CancellationToken cancellationToken = default)
    {
        var results = new Dictionary<long, string>();
        var requestUri = $"universe/names/?datasource={_options.DataSource}";

        foreach (var chunk in entityIds.Distinct().Chunk(BatchChunkSize))
        {
            var response = await PostJsonAsync<List<EsiNameResponse>>(requestUri, chunk, cancellationToken);

            if (response is null)
                continue;

            foreach (var item in response)
                results[item.Id] = item.Name;
        }

        return results;
    }

    public async Task<EsiCharacterDetails?> GetCharacterDetailsAsync(
        long characterId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await SendCountedAsync(
                () => _http.GetAsync($"characters/{characterId}/?datasource={_options.DataSource}", cancellationToken));

            if (!IsUsable(response, "character details"))
                return null;

            var dto = await response.Content.ReadFromJsonAsync<EsiCharacterResponse>(JsonOptions, cancellationToken);

            if (dto is null)
                return null;

            return new EsiCharacterDetails(
                characterId,
                dto.Name,
                dto.CorporationId,
                dto.AllianceId,
                dto.SecurityStatus,
                dto.Birthday is null ? null : DateOnly.FromDateTime(dto.Birthday.Value.UtcDateTime),
                dto.FactionId);
        }
        catch (Exception exception) when (IsCallFailure(exception, cancellationToken))
        {
            LogFailure("character details", exception);
            return null;
        }
    }

    private async Task<T?> PostJsonAsync<T>(string requestUri, object body, CancellationToken cancellationToken)
        where T : class
    {
        try
        {
            using var response = await SendCountedAsync(
                () => _http.PostAsJsonAsync(requestUri, body, JsonOptions, cancellationToken));

            if (!IsUsable(response, requestUri))
                return null;

            return await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken);
        }
        catch (Exception exception) when (IsCallFailure(exception, cancellationToken))
        {
            LogFailure(requestUri, exception);
            return null;
        }
    }

    private bool IsUsable([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] HttpResponseMessage? response, string call)
    {
        if (response is null)
        {
            _log?.Invoke($"ESI call {call} skipped: rate-limit backoff in effect");
            return false;
        }

        if (!response.IsSuccessStatusCode)
        {
            _log?.Invoke($"ESI call {call} failed: HTTP {(int)response.StatusCode}");
            return false;
        }

        return true;
    }

    private static bool IsCallFailure(Exception exception, CancellationToken cancellationToken)
    {
        return exception switch
        {
            OperationCanceledException => !cancellationToken.IsCancellationRequested,
            HttpRequestException or IOException => true,
            JsonException or NotSupportedException or InvalidOperationException => true,
            _ => false
        };
    }

    private void LogFailure(string call, Exception exception)
    {
        var cause = exception is OperationCanceledException
            ? "timed out"
            : $"{exception.GetType().Name}: {exception.Message}";

        _log?.Invoke($"ESI call {call} failed: {cause}");
    }

    private async Task<HttpResponseMessage?> SendCountedAsync(Func<Task<HttpResponseMessage>> send)
    {
        if (_utcNow().UtcTicks < Interlocked.Read(ref _backoffUntilTicks))
            return null;

        Interlocked.Increment(ref _requestCount);

        var response = await send();

        if ((int)response.StatusCode is 429 or 420)
        {
            var delay = response.Headers.RetryAfter?.Delta ?? DefaultBackoff;
            Interlocked.Exchange(ref _backoffUntilTicks, (_utcNow() + delay).UtcTicks);
        }

        return response;
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
                : DateOnly.FromDateTime(characterInfo.Birthday.Value.UtcDateTime),
            FactionId = characterInfo.FactionId
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
                _log?.Invoke($"ESI call {requestUri} failed: HTTP {(int)response.StatusCode}");
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
        catch (Exception exception) when (IsCallFailure(exception, cancellationToken))
        {
            LogFailure("exact name lookup", exception);
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
                _log?.Invoke($"ESI call {uri} failed: HTTP {(int)response.StatusCode}");
                return default;
            }
            return await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken);
        }
        catch (Exception exception) when (IsCallFailure(exception, cancellationToken))
        {
            LogFailure(uri, exception);
            return default;
        }
    }
}

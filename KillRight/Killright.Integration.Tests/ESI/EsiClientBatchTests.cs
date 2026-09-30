using System.Net;
using System.Text;
using System.Text.Json;
using Killright.Integration.Esi;
using Xunit;

namespace Killright.Integration.Tests.Esi;

public sealed class EsiClientBatchTests
{
    [Fact]
    public async Task ResolveNamesAsync_TwoNames_OneRequestWithBothNames()
    {
        var handler = new RecordingHandler((_, _) => Json("""{"characters":[{"id":95465499,"name":"T'ral Vsengne"},{"id":91321792,"name":"Lukas Naarii"}]}"""));
        var client = new EsiClient(new HttpClient(handler));

        var result = await client.ResolveNamesAsync(["t'ral vsengne", "Lukas Naarii"]);

        Assert.Single(handler.Requests);
        Assert.Contains("universe/ids", handler.Requests[0].Uri);
        Assert.Equal(EsiLookupOutcome.Matched, result["t'ral vsengne"].Outcome);
        Assert.Equal(95465499, result["t'ral vsengne"].CharacterId);
        Assert.Equal("T'ral Vsengne", result["t'ral vsengne"].CharacterName);
        Assert.Equal(91321792, result["Lukas Naarii"].CharacterId);
        Assert.Equal(1, client.RequestCount);
    }

    [Fact]
    public async Task ResolveNamesAsync_NameAbsentFromSuccessfulResponse_IsNoMatch()
    {
        var handler = new RecordingHandler((_, _) => Json("""{"characters":[{"id":91321792,"name":"Lukas Naarii"}]}"""));
        var client = new EsiClient(new HttpClient(handler));

        var result = await client.ResolveNamesAsync(["Lukas Naarii", "syMptom NZ"]);

        Assert.Equal(EsiLookupOutcome.Matched, result["Lukas Naarii"].Outcome);
        Assert.Equal(EsiLookupOutcome.NoMatch, result["syMptom NZ"].Outcome);
    }

    [Fact]
    public async Task ResolveNamesAsync_ResponseWithNoCharactersKey_AllNoMatch()
    {
        var handler = new RecordingHandler((_, _) => Json("{}"));
        var client = new EsiClient(new HttpClient(handler));

        var result = await client.ResolveNamesAsync(["syMptom NZ"]);

        Assert.Equal(EsiLookupOutcome.NoMatch, result["syMptom NZ"].Outcome);
    }

    [Fact]
    public async Task ResolveNamesAsync_OverChunkLimit_SplitsIntoChunks()
    {
        var handler = new RecordingHandler((_, _) => Json("{}"));
        var client = new EsiClient(new HttpClient(handler));
        var names = Enumerable.Range(0, EsiClient.BatchChunkSize + 1).Select(index => $"Pilot{index}").ToList();

        var result = await client.ResolveNamesAsync(names);

        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal(EsiClient.BatchChunkSize, JsonSerializer.Deserialize<string[]>(handler.Requests[0].Body!)!.Length);
        Assert.Single(JsonSerializer.Deserialize<string[]>(handler.Requests[1].Body!)!);
        Assert.Equal(names.Count, result.Count);
    }

    [Fact]
    public async Task ResolveNamesAsync_FailedChunk_MarksOnlyItsNamesFailed()
    {
        var call = 0;
        var handler = new RecordingHandler((_, _) => ++call == 1
            ? new HttpResponseMessage(HttpStatusCode.InternalServerError)
            : Json("{}"));
        var client = new EsiClient(new HttpClient(handler));
        var names = Enumerable.Range(0, EsiClient.BatchChunkSize + 1).Select(index => $"Pilot{index}").ToList();

        var result = await client.ResolveNamesAsync(names);

        Assert.Equal(EsiLookupOutcome.Failed, result["Pilot0"].Outcome);
        Assert.Equal(EsiLookupOutcome.NoMatch, result[$"Pilot{EsiClient.BatchChunkSize}"].Outcome);
    }

    [Fact]
    public async Task ResolveNamesAsync_Exception_MarksNamesFailed()
    {
        var handler = new RecordingHandler((_, _) => throw new HttpRequestException("timeout"));
        var client = new EsiClient(new HttpClient(handler));

        var result = await client.ResolveNamesAsync(["Lukas Naarii"]);

        Assert.Equal(EsiLookupOutcome.Failed, result["Lukas Naarii"].Outcome);
    }

    [Fact]
    public async Task GetAffiliationsAsync_ManyPilots_OneRequestReturnsCorporationAndAlliance()
    {
        var handler = new RecordingHandler((_, _) => Json("""[{"character_id":95465499,"corporation_id":98765,"alliance_id":99001},{"character_id":91321792,"corporation_id":98766}]"""));
        var client = new EsiClient(new HttpClient(handler));

        var result = await client.GetAffiliationsAsync([95465499, 91321792]);

        Assert.Single(handler.Requests);
        Assert.Contains("characters/affiliation", handler.Requests[0].Uri);
        Assert.Equal(99001, result[95465499].AllianceId);
        Assert.Null(result[91321792].AllianceId);
        Assert.Equal(98766, result[91321792].CorporationId);
    }

    [Fact]
    public async Task GetAffiliationsAsync_FailedRequest_ReturnsEmpty()
    {
        var handler = new RecordingHandler((_, _) => new HttpResponseMessage(HttpStatusCode.BadGateway));
        var client = new EsiClient(new HttpClient(handler));

        var result = await client.GetAffiliationsAsync([95465499]);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetEntityNamesAsync_CorporationAndAllianceIds_OneRequestReturnsNames()
    {
        var handler = new RecordingHandler((_, _) => Json("""[{"id":98765,"name":"Test Corp","category":"corporation"},{"id":99001,"name":"Test Alliance","category":"alliance"}]"""));
        var client = new EsiClient(new HttpClient(handler));

        var result = await client.GetEntityNamesAsync([98765, 99001, 98765]);

        Assert.Single(handler.Requests);
        Assert.Contains("universe/names", handler.Requests[0].Uri);
        Assert.Equal("Test Corp", result[98765]);
        Assert.Equal("Test Alliance", result[99001]);
    }

    [Fact]
    public async Task GetEntityNamesAsync_FailedRequest_ReturnsEmpty()
    {
        var handler = new RecordingHandler((_, _) => new HttpResponseMessage(HttpStatusCode.NotFound));
        var client = new EsiClient(new HttpClient(handler));

        var result = await client.GetEntityNamesAsync([98765]);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetCharacterDetailsAsync_Success_ReturnsSecurityStatusBirthdayAndAffiliation()
    {
        var handler = new RecordingHandler((_, _) => Json("""{"name":"T'ral Vsengne","corporation_id":98765,"alliance_id":99001,"security_status":-1.2,"birthday":"2015-06-12T10:00:00Z"}"""));
        var client = new EsiClient(new HttpClient(handler));

        var details = await client.GetCharacterDetailsAsync(95465499);

        Assert.NotNull(details);
        Assert.Equal("T'ral Vsengne", details!.Name);
        Assert.Equal(98765, details.CorporationId);
        Assert.Equal(99001, details.AllianceId);
        Assert.Equal(-1.2, details.SecurityStatus);
        Assert.Equal(new DateOnly(2015, 6, 12), details.Birthday);
    }

    [Fact]
    public async Task GetCharacterDetailsAsync_ServerError_ReturnsNull()
    {
        var handler = new RecordingHandler((_, _) => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var client = new EsiClient(new HttpClient(handler));

        Assert.Null(await client.GetCharacterDetailsAsync(95465499));
    }

    [Fact]
    public async Task GetCharacterDetailsAsync_TooManyRequests_BacksOffUntilRetryAfterElapses()
    {
        var now = new DateTimeOffset(2026, 9, 30, 12, 0, 0, TimeSpan.Zero);
        var call = 0;
        var handler = new RecordingHandler((_, _) =>
        {
            call++;

            if (call == 1)
            {
                var limited = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
                limited.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(30));
                return limited;
            }

            return Json("""{"name":"Lukas Naarii","corporation_id":98765,"security_status":0.4,"birthday":"2016-01-02T00:00:00Z"}""");
        });
        var client = new EsiClient(new HttpClient(handler), utcNow: () => now);

        Assert.Null(await client.GetCharacterDetailsAsync(91321792));
        Assert.Null(await client.GetCharacterDetailsAsync(91321792));
        Assert.Equal(1, handler.Requests.Count);
        Assert.Equal(1, client.RequestCount);

        now = now.AddSeconds(31);

        Assert.NotNull(await client.GetCharacterDetailsAsync(91321792));
        Assert.Equal(2, handler.Requests.Count);
    }

    private static HttpResponseMessage Json(string body)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, string?, HttpResponseMessage> _respond;

        public RecordingHandler(Func<HttpRequestMessage, string?, HttpResponseMessage> respond)
        {
            _respond = respond;
        }

        public List<(string Uri, string? Body)> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);

            lock (Requests)
                Requests.Add((request.RequestUri!.ToString(), body));

            return _respond(request, body);
        }
    }
}

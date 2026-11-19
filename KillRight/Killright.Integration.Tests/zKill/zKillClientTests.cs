using System.Net;
using Killright.Integration.Tests.Esi;
using Killright.Integration.zKill;
using Killright.Shared.zKill;
using Xunit;

namespace Killright.Integration.Tests.zKill;

public sealed class zKillClientTests
{
    [Fact]
    public async Task GetStatisticsAsync_SuccessWithMonths_ReturnsSuccessOutcomeWithParsedMonths()
    {
        var handler = new ScriptedHttpMessageHandler()
            .OnUriContaining("api/stats/characterID/95465499", HttpStatusCode.OK, """
                {"shipsDestroyed":42,"soloKills":10,"soloRatio":55.5,"avgGangSize":3.2,"shipsLost":8,"soloLosses":1,
                 "months":{"202608":{"year":2026,"month":8,"shipsLost":1,"shipsDestroyed":5}}}
                """);

        var client = new zKillClient(new HttpClient(handler));

        var result = await client.GetStatisticsAsync(95465499);

        Assert.Equal(zKillStatisticsOutcome.Success, result.Outcome);
        Assert.NotNull(result.Statistics);
        Assert.Equal(42, result.Statistics!.shipsDestroyed);
        Assert.NotNull(result.Statistics.months);
        Assert.True(result.Statistics.months!.ContainsKey("202608"));
        Assert.Equal(5, result.Statistics.months["202608"].ShipsDestroyed);
    }

    [Fact]
    public async Task GetStatisticsAsync_SuccessWithCapsuleGroup_ExtractsPodKills()
    {
        var handler = new ScriptedHttpMessageHandler()
            .OnUriContaining("api/stats/characterID/95465499", HttpStatusCode.OK, """
                {"shipsDestroyed":42,"soloKills":10,"soloRatio":55.5,"avgGangSize":3.2,"shipsLost":8,"soloLosses":1,
                 "groups":{"29":{"groupID":29,"shipsLost":13,"pointsLost":13,"iskLost":182779801,"shipsDestroyed":39,"pointsDestroyed":39,"iskDestroyed":1127784}}}
                """);

        var client = new zKillClient(new HttpClient(handler));

        var result = await client.GetStatisticsAsync(95465499);

        Assert.Equal(zKillStatisticsOutcome.Success, result.Outcome);
        Assert.NotNull(result.Statistics);
        Assert.Equal(39, result.Statistics!.podKills);
    }

    [Fact]
    public async Task GetStatisticsAsync_SuccessWithCapsuleGroup_ExtractsPodLosses()
    {
        var handler = new ScriptedHttpMessageHandler()
            .OnUriContaining("api/stats/characterID/95465499", HttpStatusCode.OK, """
                {"shipsDestroyed":42,"soloKills":10,"soloRatio":55.5,"avgGangSize":3.2,"shipsLost":20,"soloLosses":1,
                 "groups":{"29":{"groupID":29,"shipsLost":13,"pointsLost":13,"iskLost":182779801,"shipsDestroyed":39,"pointsDestroyed":39,"iskDestroyed":1127784}}}
                """);

        var client = new zKillClient(new HttpClient(handler));

        var result = await client.GetStatisticsAsync(95465499);

        Assert.Equal(13, result.Statistics!.podLosses);
        Assert.Equal(20, result.Statistics.shipsLost);
    }

    [Fact]
    public async Task GetStatisticsAsync_CapturedLukasNaariiResponse_ExtractsPodKillsAndPodLosses()
    {
        var json = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "zKill", "Fixtures", "stats-lukas-naarii.json"));
        var handler = new ScriptedHttpMessageHandler()
            .OnUriContaining("api/stats/characterID/2116955190", HttpStatusCode.OK, json);

        var client = new zKillClient(new HttpClient(handler));

        var result = await client.GetStatisticsAsync(2116955190);

        Assert.Equal(zKillStatisticsOutcome.Success, result.Outcome);
        Assert.Equal(136, result.Statistics!.podKills);
        Assert.Equal(99, result.Statistics.podLosses);
        Assert.Equal(355, result.Statistics.shipsLost);
    }

    [Fact]
    public async Task GetStatisticsAsync_SuccessWithoutCapsuleGroup_PodLossesIsZero()
    {
        var handler = new ScriptedHttpMessageHandler()
            .OnUriContaining("api/stats/characterID/91321792", HttpStatusCode.OK, """{"shipsDestroyed":5,"soloKills":1,"soloRatio":20,"avgGangSize":2,"shipsLost":3,"soloLosses":0}""");

        var client = new zKillClient(new HttpClient(handler));

        var result = await client.GetStatisticsAsync(91321792);

        Assert.Equal(0, result.Statistics!.podLosses);
    }

    [Fact]
    public async Task GetStatisticsAsync_SuccessWithoutCapsuleGroup_PodKillsIsZero()
    {
        var handler = new ScriptedHttpMessageHandler()
            .OnUriContaining("api/stats/characterID/91321792", HttpStatusCode.OK, """{"shipsDestroyed":5,"soloKills":1,"soloRatio":20,"avgGangSize":2,"shipsLost":0,"soloLosses":0}""");

        var client = new zKillClient(new HttpClient(handler));

        var result = await client.GetStatisticsAsync(91321792);

        Assert.Equal(zKillStatisticsOutcome.Success, result.Outcome);
        Assert.NotNull(result.Statistics);
        Assert.Equal(0, result.Statistics!.podKills);
    }

    [Fact]
    public async Task GetStatisticsAsync_SuccessWithoutMonths_ReturnsSuccessWithNullMonths()
    {
        var handler = new ScriptedHttpMessageHandler()
            .OnUriContaining("api/stats/characterID/91321792", HttpStatusCode.OK, """{"shipsDestroyed":0,"soloKills":0,"soloRatio":0,"avgGangSize":0,"shipsLost":0,"soloLosses":0}""");

        var client = new zKillClient(new HttpClient(handler));

        var result = await client.GetStatisticsAsync(91321792);

        Assert.Equal(zKillStatisticsOutcome.Success, result.Outcome);
        Assert.NotNull(result.Statistics);
        Assert.Null(result.Statistics!.months);
    }

    [Fact]
    public async Task GetStatisticsAsync_NoHistoryResponse_ReturnsNoHistoryOutcome()
    {
        var handler = new ScriptedHttpMessageHandler()
            .OnUriContaining("api/stats/characterID/98798418", HttpStatusCode.OK, """{"error":"Invalid type or id"}""");

        var client = new zKillClient(new HttpClient(handler));

        var result = await client.GetStatisticsAsync(98798418);

        Assert.Equal(zKillStatisticsOutcome.NoHistory, result.Outcome);
        Assert.Null(result.Statistics);
    }

    [Fact]
    public async Task GetStatisticsAsync_HttpFailure_ReturnsFailureOutcome()
    {
        var handler = new ScriptedHttpMessageHandler()
            .OnUriContaining("api/stats/characterID/91321792", HttpStatusCode.InternalServerError);

        var client = new zKillClient(new HttpClient(handler));

        var result = await client.GetStatisticsAsync(91321792);

        Assert.Equal(zKillStatisticsOutcome.Failure, result.Outcome);
        Assert.Null(result.Statistics);
    }

    [Fact]
    public async Task GetStatisticsAsync_Throws_ReturnsFailureOutcome()
    {
        var handler = new ScriptedHttpMessageHandler()
            .ThrowOnUriContaining("api/stats/characterID/91321792", new HttpRequestException());

        var client = new zKillClient(new HttpClient(handler));

        var result = await client.GetStatisticsAsync(91321792);

        Assert.Equal(zKillStatisticsOutcome.Failure, result.Outcome);
    }

    [Fact]
    public async Task GetRecentKillmailsAsync_SuccessWithKillmails_ReturnsSuccessOutcome()
    {
        var handler = new ScriptedHttpMessageHandler()
            .OnUriContaining("api/characterID/95465499/pastSeconds", HttpStatusCode.OK, """
                [{"killmail_id":123456,"killmail_time":"2026-09-20T10:00:00Z","solar_system_id":30000142,
                  "victim":{"character_id":999,"ship_type_id":587},
                  "attackers":[{"character_id":95465499,"corporation_id":98000001,"alliance_id":99000001,"ship_type_id":11567,"weapon_type_id":3074}],
                  "zkb":{"hash":"abc123","locationID":40000001,"solo":true,"npc":false}}]
                """);

        var client = new zKillClient(new HttpClient(handler));

        var result = await client.GetRecentKillmailsAsync(95465499, 604800);

        Assert.Equal(zKillRecentKillmailOutcome.Success, result.Outcome);

        Assert.Single(result.RawKillmails);
        var raw = result.RawKillmails[0];
        Assert.Equal(123456, raw.KillmailId);
        Assert.Equal(999, raw.VictimCharacterId);
        Assert.Equal(587, raw.VictimShipTypeId);
        Assert.Equal(30000142, raw.SystemId);
        Assert.Single(raw.Attackers);
        Assert.Equal(95465499, raw.Attackers[0].CharacterId);
        Assert.Equal(98000001, raw.Attackers[0].CorporationId);
        Assert.Equal(99000001, raw.Attackers[0].AllianceId);
        Assert.Equal(3074, raw.Attackers[0].WeaponTypeId);
    }

    [Theory]
    [InlineData("\"victim\":null,\"attackers\":[],\"zkb\":{\"hash\":\"a\"}")]
    [InlineData("\"victim\":{\"character_id\":1},\"attackers\":null,\"zkb\":{\"hash\":\"a\"}")]
    [InlineData("\"victim\":{\"character_id\":1},\"attackers\":[],\"zkb\":null")]
    [InlineData("\"victim\":{\"character_id\":1},\"attackers\":[null],\"zkb\":{\"hash\":\"a\"}")]
    public async Task GetRecentKillmailsAsync_NullPayloadField_ReturnsFailureOutcome(string fields)
    {
        var handler = new ScriptedHttpMessageHandler()
            .OnUriContaining("api/characterID/95465499/pastSeconds", HttpStatusCode.OK,
                "[{\"killmail_id\":1,\"killmail_time\":\"2026-09-20T10:00:00Z\",\"solar_system_id\":1," + fields + "}]");

        var client = new zKillClient(new HttpClient(handler));

        var result = await client.GetRecentKillmailsAsync(95465499, 3600);

        Assert.Equal(zKillRecentKillmailOutcome.Failure, result.Outcome);
        Assert.Empty(result.RawKillmails);
    }

    [Fact]
    public async Task GetStatisticsAsync_NullGroupEntry_ReturnsFailureOutcome()
    {
        var handler = new ScriptedHttpMessageHandler()
            .OnUriContaining("api/stats/characterID/91321792", HttpStatusCode.OK, "{\"groups\":{\"29\":null}}");

        var client = new zKillClient(new HttpClient(handler));

        var result = await client.GetStatisticsAsync(91321792);

        Assert.Equal(zKillStatisticsOutcome.Failure, result.Outcome);
    }

    [Fact]
    public async Task GetRecentKillmailsAsync_EmptyArray_ReturnsSuccessWithNoKillmails()
    {
        var handler = new ScriptedHttpMessageHandler()
            .OnUriContaining("api/characterID/91321792/pastSeconds", HttpStatusCode.OK, "[]");

        var client = new zKillClient(new HttpClient(handler));

        var result = await client.GetRecentKillmailsAsync(91321792, 3600);

        Assert.Equal(zKillRecentKillmailOutcome.Success, result.Outcome);
        Assert.Empty(result.RawKillmails);
    }

    [Fact]
    public async Task GetRecentKillmailsAsync_NoContent_ReturnsSuccessWithNoKillmails()
    {
        var handler = new ScriptedHttpMessageHandler()
            .OnUriContaining("api/characterID/91321792/pastSeconds", HttpStatusCode.NoContent);

        var client = new zKillClient(new HttpClient(handler));

        var result = await client.GetRecentKillmailsAsync(91321792, 3600);

        Assert.Equal(zKillRecentKillmailOutcome.Success, result.Outcome);
        Assert.Empty(result.RawKillmails);
    }

    [Fact]
    public async Task GetRecentKillmailsAsync_NoHistoryResponse_ReturnsNoHistoryOutcome()
    {
        var handler = new ScriptedHttpMessageHandler()
            .OnUriContaining("api/characterID/98798418/pastSeconds", HttpStatusCode.OK, """{"error":"Invalid type or id"}""");

        var client = new zKillClient(new HttpClient(handler));

        var result = await client.GetRecentKillmailsAsync(98798418, 604800);

        Assert.Equal(zKillRecentKillmailOutcome.NoHistory, result.Outcome);
        Assert.Empty(result.RawKillmails);
    }

    [Fact]
    public async Task GetRecentKillmailsAsync_HttpFailure_ReturnsFailureOutcome()
    {
        var handler = new ScriptedHttpMessageHandler()
            .OnUriContaining("api/characterID/91321792/pastSeconds", HttpStatusCode.InternalServerError);

        var client = new zKillClient(new HttpClient(handler));

        var result = await client.GetRecentKillmailsAsync(91321792, 3600);

        Assert.Equal(zKillRecentKillmailOutcome.Failure, result.Outcome);
    }

    [Fact]
    public async Task GetRecentKillmailsAsync_MultiplePages_MergesAllPagesInOrderAndCountsEachAgainstBudget()
    {
        var handler = new ScriptedHttpMessageHandler()
            .OnUriContaining("pastSeconds/3600/page/3/", HttpStatusCode.OK, "[]")
            .OnUriContaining("pastSeconds/3600/page/2/", HttpStatusCode.OK, KillmailPage(3, 4))
            .OnUriContaining("pastSeconds/3600/", HttpStatusCode.OK, KillmailPage(1, 2));
        var limiter = new CountingLimiter();
        var client = new zKillClient(new HttpClient(handler), limiter: limiter);

        var result = await client.GetRecentKillmailsAsync(95465499, 3600);

        Assert.Equal(zKillRecentKillmailOutcome.Success, result.Outcome);
        Assert.Equal([1L, 2L, 3L, 4L], result.RawKillmails.Select(k => k.KillmailId).ToArray());
        Assert.Equal(3, limiter.Calls);
        Assert.Equal(3, client.RequestCount);
    }

    [Fact]
    public async Task GetRecentKillmailsAsync_SecondPageFails_ReturnsFailureWithNothing()
    {
        var handler = new ScriptedHttpMessageHandler()
            .OnUriContaining("pastSeconds/3600/page/2/", HttpStatusCode.InternalServerError)
            .OnUriContaining("pastSeconds/3600/", HttpStatusCode.OK, KillmailPage(1, 2));
        var client = new zKillClient(new HttpClient(handler));

        var result = await client.GetRecentKillmailsAsync(95465499, 3600);

        Assert.Equal(zKillRecentKillmailOutcome.Failure, result.Outcome);
        Assert.Empty(result.RawKillmails);
    }

    [Fact]
    public async Task GetRecentKillmailsAsync_RateLimitedOnSecondPage_PausesAndReturnsFailure()
    {
        var handler = new ScriptedHttpMessageHandler()
            .OnUriContaining("pastSeconds/3600/page/2/", HttpStatusCode.TooManyRequests)
            .OnUriContaining("pastSeconds/3600/", HttpStatusCode.OK, KillmailPage(1, 2));
        var limiter = new CountingLimiter();
        var client = new zKillClient(new HttpClient(handler), limiter: limiter);

        var result = await client.GetRecentKillmailsAsync(95465499, 3600);

        Assert.Equal(zKillRecentKillmailOutcome.Failure, result.Outcome);
        Assert.Equal(1, client.RateLimitedCount);
        Assert.Equal(1, limiter.Pauses);
    }

    [Fact]
    public async Task GetLastKillmailAsync_MultiPageReply_ReadsOnePage()
    {
        var handler = new ScriptedHttpMessageHandler()
            .OnUriContaining("page/2/", HttpStatusCode.OK, KillmailPage(3, 4))
            .OnUriContaining("api/characterID/95465499/", HttpStatusCode.OK, KillmailPage(1, 2));
        var client = new zKillClient(new HttpClient(handler));

        var result = await client.GetLastKillmailAsync(95465499);

        Assert.Equal(zKillLastKillmailOutcome.Success, result.Outcome);
        Assert.Equal(1, client.RequestCount);
    }

    private static string KillmailPage(params long[] ids)
    {
        var items = ids.Select(id => $$$"""
            {"killmail_id":{{{id}}},"killmail_time":"2026-09-20T10:00:00Z","solar_system_id":30000142,
             "victim":{"character_id":999,"ship_type_id":587},"attackers":[],
             "zkb":{"hash":"h{{{id}}}","locationID":40000001,"solo":true,"npc":false}}
            """);

        return "[" + string.Join(",", items) + "]";
    }

    private sealed class CountingLimiter : Killright.Integration.RateLimiting.IRequestStartLimiter
    {
        public int Calls { get; private set; }

        public int Pauses { get; private set; }

        public long TotalWaitMilliseconds => 0;

        public bool IsPaused => false;

        public void Pause(TimeSpan duration) => Pauses++;

        public Task WaitAsync(CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task GetRecentKillmailsAsync_Throws_ReturnsFailureOutcome()
    {
        var handler = new ScriptedHttpMessageHandler()
            .ThrowOnUriContaining("api/characterID/91321792/pastSeconds", new HttpRequestException());

        var client = new zKillClient(new HttpClient(handler));

        var result = await client.GetRecentKillmailsAsync(91321792, 3600);

        Assert.Equal(zKillRecentKillmailOutcome.Failure, result.Outcome);
    }
}

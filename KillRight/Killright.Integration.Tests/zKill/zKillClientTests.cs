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
                  "attackers":[{"character_id":95465499,"ship_type_id":11567}],
                  "zkb":{"hash":"abc123","locationID":40000001,"solo":true,"npc":false}}]
                """);

        var client = new zKillClient(new HttpClient(handler));

        var result = await client.GetRecentKillmailsAsync(95465499, 604800);

        Assert.Equal(zKillRecentKillmailOutcome.Success, result.Outcome);
        Assert.Single(result.Killmails);
        Assert.Equal(123456, result.Killmails[0].KillmailId);
        Assert.False(result.Killmails[0].IsLoss);
    }

    [Fact]
    public async Task GetRecentKillmailsAsync_EmptyArray_ReturnsSuccessWithNoKillmails()
    {
        var handler = new ScriptedHttpMessageHandler()
            .OnUriContaining("api/characterID/91321792/pastSeconds", HttpStatusCode.OK, "[]");

        var client = new zKillClient(new HttpClient(handler));

        var result = await client.GetRecentKillmailsAsync(91321792, 3600);

        Assert.Equal(zKillRecentKillmailOutcome.Success, result.Outcome);
        Assert.Empty(result.Killmails);
    }

    [Fact]
    public async Task GetRecentKillmailsAsync_NoContent_ReturnsSuccessWithNoKillmails()
    {
        var handler = new ScriptedHttpMessageHandler()
            .OnUriContaining("api/characterID/91321792/pastSeconds", HttpStatusCode.NoContent);

        var client = new zKillClient(new HttpClient(handler));

        var result = await client.GetRecentKillmailsAsync(91321792, 3600);

        Assert.Equal(zKillRecentKillmailOutcome.Success, result.Outcome);
        Assert.Empty(result.Killmails);
    }

    [Fact]
    public async Task GetRecentKillmailsAsync_NoHistoryResponse_ReturnsNoHistoryOutcome()
    {
        var handler = new ScriptedHttpMessageHandler()
            .OnUriContaining("api/characterID/98798418/pastSeconds", HttpStatusCode.OK, """{"error":"Invalid type or id"}""");

        var client = new zKillClient(new HttpClient(handler));

        var result = await client.GetRecentKillmailsAsync(98798418, 604800);

        Assert.Equal(zKillRecentKillmailOutcome.NoHistory, result.Outcome);
        Assert.Empty(result.Killmails);
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
    public async Task GetRecentKillmailsAsync_Throws_ReturnsFailureOutcome()
    {
        var handler = new ScriptedHttpMessageHandler()
            .ThrowOnUriContaining("api/characterID/91321792/pastSeconds", new HttpRequestException());

        var client = new zKillClient(new HttpClient(handler));

        var result = await client.GetRecentKillmailsAsync(91321792, 3600);

        Assert.Equal(zKillRecentKillmailOutcome.Failure, result.Outcome);
    }
}

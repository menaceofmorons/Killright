using System.Net;
using Killright.Integration.RateLimiting;
using Killright.Integration.Tests.Esi;
using Killright.Integration.zKill;
using Killright.Shared.zKill;
using Xunit;

namespace Killright.Integration.Tests.zKill;

public sealed class zKillClientLastKillmailTests
{
    private const long Lukas = 2116955190;
    private const long Pod = 670;
    private const long Punisher = 623;

    [Fact]
    public async Task GetLastKillmailAsync_CombinedFixture_SkipsPodLossAndReturnsNextEntryAsLoss()
    {
        var handler = new ScriptedHttpMessageHandler()
            .OnUriContaining($"api/characterID/{Lukas}/", HttpStatusCode.OK, ReadFixture("characterID-combined.json"));
        var client = new zKillClient(new HttpClient(handler));

        var result = await client.GetLastKillmailAsync(Lukas);

        Assert.Equal(zKillLastKillmailOutcome.Success, result.Outcome);
        Assert.Equal(zKillActivityType.Loss, result.ActivityType);
        Assert.Equal(138775003, result.Killmail!.KillmailId);
        Assert.Equal(Punisher, result.Killmail.VictimShipTypeId);
        Assert.Equal(Lukas, result.Killmail.VictimCharacterId);
        Assert.Equal(new DateTimeOffset(2026, 9, 28, 20, 51, 14, TimeSpan.Zero), result.Killmail.KillTimeUtc);
    }

    [Fact]
    public async Task GetLastKillmailAsync_KillsFixture_SkipsPodKillsAndReturnsKillWithPilotAttackerEntry()
    {
        var handler = new ScriptedHttpMessageHandler()
            .OnUriContaining($"api/characterID/{Lukas}/", HttpStatusCode.OK, ReadFixture("kills-only.json"));
        var client = new zKillClient(new HttpClient(handler));

        var result = await client.GetLastKillmailAsync(Lukas);

        Assert.Equal(zKillLastKillmailOutcome.Success, result.Outcome);
        Assert.Equal(zKillActivityType.Kill, result.ActivityType);
        Assert.Equal(138772243, result.Killmail!.KillmailId);
        Assert.Equal(2, result.Killmail.Attackers.Count);

        var own = result.Killmail.Attackers.Single(attacker => attacker.CharacterId == Lukas);
        Assert.Equal(Punisher, own.ShipTypeId);
        Assert.Equal(3138, own.WeaponTypeId);
    }

    [Fact]
    public async Task GetLastKillmailAsync_LossesFixture_ReturnsLoss()
    {
        var handler = new ScriptedHttpMessageHandler()
            .OnUriContaining($"api/characterID/{Lukas}/", HttpStatusCode.OK, ReadFixture("losses-only.json"));
        var client = new zKillClient(new HttpClient(handler));

        var result = await client.GetLastKillmailAsync(Lukas);

        Assert.Equal(zKillActivityType.Loss, result.ActivityType);
        Assert.Equal(138775003, result.Killmail!.KillmailId);
    }

    [Fact]
    public async Task GetLastKillmailAsync_FirstEntryIsPodSecondIsNot_UsesSecondAndSendsOneRequest()
    {
        var handler = new ScriptedHttpMessageHandler()
            .OnUriContaining($"api/characterID/{Lukas}/", HttpStatusCode.OK,
                List(Entry(1, 500, Pod, Lukas), Entry(2, Lukas, Punisher, 501)));
        var client = new zKillClient(new HttpClient(handler));

        var result = await client.GetLastKillmailAsync(Lukas);

        Assert.Equal(2, result.Killmail!.KillmailId);
        Assert.Equal(1, client.RequestCount);
    }

    [Fact]
    public async Task GetLastKillmailAsync_VictimIsNotThePilot_ReturnsKill()
    {
        var handler = new ScriptedHttpMessageHandler()
            .OnUriContaining($"api/characterID/{Lukas}/", HttpStatusCode.OK,
                List(Entry(7, 500, Punisher, Lukas)));
        var client = new zKillClient(new HttpClient(handler));

        var result = await client.GetLastKillmailAsync(Lukas);

        Assert.Equal(zKillActivityType.Kill, result.ActivityType);
    }

    [Fact]
    public async Task GetLastKillmailAsync_PageOfOnlyPods_RequestsPageTwo()
    {
        var handler = new ScriptedHttpMessageHandler()
            .OnUriContaining($"api/characterID/{Lukas}/page/2/", HttpStatusCode.OK, List(Entry(3, Lukas, Punisher, 501)))
            .OnUriContaining($"api/characterID/{Lukas}/", HttpStatusCode.OK, List(Entry(1, 500, Pod, Lukas), Entry(2, 500, Pod, Lukas)));
        var client = new zKillClient(new HttpClient(handler));

        var result = await client.GetLastKillmailAsync(Lukas);

        Assert.Equal(zKillLastKillmailOutcome.Success, result.Outcome);
        Assert.Equal(3, result.Killmail!.KillmailId);
        Assert.Equal(2, client.RequestCount);
    }

    [Fact]
    public async Task GetLastKillmailAsync_PageBoundExhaustedWithPodsOnly_ReturnsFailure()
    {
        var pods = List(Entry(1, 500, Pod, Lukas));
        var handler = new ScriptedHttpMessageHandler()
            .OnUriContaining($"api/characterID/{Lukas}/page/3/", HttpStatusCode.OK, pods)
            .OnUriContaining($"api/characterID/{Lukas}/page/2/", HttpStatusCode.OK, pods)
            .OnUriContaining($"api/characterID/{Lukas}/", HttpStatusCode.OK, pods);
        var client = new zKillClient(new HttpClient(handler));

        var result = await client.GetLastKillmailAsync(Lukas);

        Assert.Equal(zKillLastKillmailOutcome.Failure, result.Outcome);
        Assert.Null(result.Killmail);
        Assert.Equal(3, client.RequestCount);
    }

    [Fact]
    public async Task GetLastKillmailAsync_PodOnlyPageThenEmptyPage_ReturnsSuccessWithNoKillmail()
    {
        var handler = new ScriptedHttpMessageHandler()
            .OnUriContaining($"api/characterID/{Lukas}/page/2/", HttpStatusCode.OK, "[]")
            .OnUriContaining($"api/characterID/{Lukas}/", HttpStatusCode.OK, List(Entry(1, 500, Pod, Lukas)));
        var client = new zKillClient(new HttpClient(handler));

        var result = await client.GetLastKillmailAsync(Lukas);

        Assert.Equal(zKillLastKillmailOutcome.Success, result.Outcome);
        Assert.Null(result.Killmail);
        Assert.Null(result.ActivityType);
        Assert.Equal(2, client.RequestCount);
    }

    [Fact]
    public async Task GetLastKillmailAsync_EmptyList_ReturnsSuccessWithNoKillmail()
    {
        var handler = new ScriptedHttpMessageHandler()
            .OnUriContaining($"api/characterID/{Lukas}/", HttpStatusCode.OK, "[]");
        var client = new zKillClient(new HttpClient(handler));

        var result = await client.GetLastKillmailAsync(Lukas);

        Assert.Equal(zKillLastKillmailOutcome.Success, result.Outcome);
        Assert.Null(result.Killmail);
        Assert.Equal(1, client.RequestCount);
    }

    [Fact]
    public async Task GetLastKillmailAsync_InvalidTypeOrIdResponse_ReturnsNoHistory()
    {
        var handler = new ScriptedHttpMessageHandler()
            .OnUriContaining($"api/characterID/{Lukas}/", HttpStatusCode.OK, """{"error":"Invalid type or id"}""");
        var client = new zKillClient(new HttpClient(handler));

        var result = await client.GetLastKillmailAsync(Lukas);

        Assert.Equal(zKillLastKillmailOutcome.NoHistory, result.Outcome);
        Assert.Null(result.Killmail);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.NotFound)]
    public async Task GetLastKillmailAsync_HttpFailure_ReturnsFailure(HttpStatusCode statusCode)
    {
        var handler = new ScriptedHttpMessageHandler()
            .OnUriContaining($"api/characterID/{Lukas}/", statusCode);
        var client = new zKillClient(new HttpClient(handler));

        var result = await client.GetLastKillmailAsync(Lukas);

        Assert.Equal(zKillLastKillmailOutcome.Failure, result.Outcome);
    }

    [Fact]
    public async Task GetLastKillmailAsync_UnreadableBody_ReturnsFailure()
    {
        var handler = new ScriptedHttpMessageHandler()
            .OnUriContaining($"api/characterID/{Lukas}/", HttpStatusCode.OK, "not json");
        var client = new zKillClient(new HttpClient(handler));

        var result = await client.GetLastKillmailAsync(Lukas);

        Assert.Equal(zKillLastKillmailOutcome.Failure, result.Outcome);
    }

    [Fact]
    public async Task GetLastKillmailAsync_Throws_ReturnsFailure()
    {
        var handler = new ScriptedHttpMessageHandler()
            .ThrowOnUriContaining($"api/characterID/{Lukas}/", new HttpRequestException());
        var client = new zKillClient(new HttpClient(handler));

        var result = await client.GetLastKillmailAsync(Lukas);

        Assert.Equal(zKillLastKillmailOutcome.Failure, result.Outcome);
    }

    [Fact]
    public async Task GetLastKillmailAsync_RequestUris_EndWithTrailingSlashAndCarryNoPastSeconds()
    {
        var handler = new RecordingHandler(List(Entry(1, 500, Pod, Lukas)), "[]");
        var client = new zKillClient(new HttpClient(handler));

        await client.GetLastKillmailAsync(Lukas);

        Assert.Equal([$"/api/characterID/{Lukas}/", $"/api/characterID/{Lukas}/page/2/"], handler.Paths);
    }

    [Fact]
    public async Task GetLastKillmailAsync_EachRequestPassesThroughLimiter()
    {
        var handler = new ScriptedHttpMessageHandler()
            .OnUriContaining($"api/characterID/{Lukas}/page/2/", HttpStatusCode.OK, List(Entry(3, Lukas, Punisher, 501)))
            .OnUriContaining($"api/characterID/{Lukas}/", HttpStatusCode.OK, List(Entry(1, 500, Pod, Lukas)));
        var limiter = new CountingLimiter();
        var client = new zKillClient(new HttpClient(handler), limiter: limiter);

        await client.GetLastKillmailAsync(Lukas);

        Assert.Equal(2, limiter.Calls);
        Assert.Equal(2, client.RequestCount);
    }

    private static string ReadFixture(string name)
    {
        return File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "zKill", "Fixtures", name));
    }

    private static string List(params string[] entries)
    {
        return "[" + string.Join(",", entries) + "]";
    }

    private static string Entry(long killmailId, long victimCharacterId, long victimShipTypeId, long attackerCharacterId)
    {
        return """
            {"killmail_id":{killmailId},"killmail_time":"2026-09-20T10:00:00Z","solar_system_id":30000142,
             "victim":{"character_id":{victimCharacterId},"ship_type_id":{victimShipTypeId}},
             "attackers":[{"character_id":{attackerCharacterId},"corporation_id":98000001,"ship_type_id":11567,"weapon_type_id":3074}],
             "zkb":{"hash":"h{killmailId}","locationID":40000001,"solo":true,"npc":false}}
            """.Replace("{killmailId}", killmailId.ToString()).Replace("{victimCharacterId}", victimCharacterId.ToString()).Replace("{victimShipTypeId}", victimShipTypeId.ToString()).Replace("{attackerCharacterId}", attackerCharacterId.ToString());
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly Queue<string> _bodies;

        public RecordingHandler(params string[] bodies) => _bodies = new Queue<string>(bodies);

        public List<string> Paths { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Paths.Add(request.RequestUri!.AbsolutePath);

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_bodies.Dequeue(), System.Text.Encoding.UTF8, "application/json")
            });
        }
    }

    private sealed class CountingLimiter : IRequestStartLimiter
    {
        public int Calls { get; private set; }

        public long TotalWaitMilliseconds => 0;

        public Task WaitAsync(CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.CompletedTask;
        }
    }
}

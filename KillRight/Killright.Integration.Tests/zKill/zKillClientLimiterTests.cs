using System.Net;
using Killright.Integration.RateLimiting;
using Killright.Integration.Tests.Esi;
using Killright.Integration.zKill;
using Killright.Shared.zKill;
using Xunit;

namespace Killright.Integration.Tests.zKill;

public sealed class zKillClientLimiterTests
{
    [Fact]
    public async Task GetStatisticsAndRecentKillmails_WithLimiter_EachRequestPassesThroughLimiter()
    {
        var handler = new ScriptedHttpMessageHandler()
            .OnUriContaining("api/stats/characterID/95465499", HttpStatusCode.OK, """{"shipsDestroyed":1,"soloKills":0,"soloRatio":0,"avgGangSize":2,"shipsLost":0,"soloLosses":0}""")
            .OnUriContaining("api/characterID/95465499", HttpStatusCode.OK, "[]");
        var limiter = new CountingLimiter();
        var client = new zKillClient(new HttpClient(handler), limiter: limiter);

        await client.GetStatisticsAsync(95465499);
        await client.GetRecentKillmailsAsync(95465499, 3600);

        Assert.Equal(2, limiter.Calls);
        Assert.Equal(2, client.RequestCount);
    }

    [Fact]
    public async Task GetStatisticsAsync_WithBudget_RequestBeyondBudgetWaitsForTheWindowAndStillSucceeds()
    {
        var now = 0L;
        var waits = new List<TimeSpan>();
        var budget = new RollingWindowRequestBudget(2, TimeSpan.FromSeconds(60), () => now, (span, _) =>
        {
            waits.Add(span);
            now += span.Ticks;
            return Task.CompletedTask;
        });
        var handler = new ScriptedHttpMessageHandler()
            .OnUriContaining("api/stats/characterID/95465499", HttpStatusCode.OK, """{"shipsDestroyed":1,"soloKills":0,"soloRatio":0,"avgGangSize":2,"shipsLost":0,"soloLosses":0}""");
        var client = new zKillClient(new HttpClient(handler), limiter: budget);

        for (var call = 0; call < 3; call++)
        {
            var result = await client.GetStatisticsAsync(95465499);
            Assert.Equal(zKillStatisticsOutcome.Success, result.Outcome);
        }

        Assert.Equal([TimeSpan.FromSeconds(60)], waits);
        Assert.Equal(3, client.RequestCount);
        Assert.Equal(60_000, budget.TotalWaitMilliseconds);
    }

    [Fact]
    public async Task GetStatisticsAsync_LimiterWaitCancelled_ReturnsFailureAndSendsNoRequest()
    {
        var handler = new ScriptedHttpMessageHandler();
        var client = new zKillClient(new HttpClient(handler), limiter: new CancellingLimiter());

        var result = await client.GetStatisticsAsync(95465499);

        Assert.Equal(zKillStatisticsOutcome.Failure, result.Outcome);
        Assert.Equal(0, client.RequestCount);
    }

    [Fact]
    public async Task GetStatisticsAsync_NoLimiter_StillCountsRequests()
    {
        var handler = new ScriptedHttpMessageHandler()
            .OnUriContaining("api/stats/characterID/95465499", HttpStatusCode.TooManyRequests);
        var client = new zKillClient(new HttpClient(handler));

        var result = await client.GetStatisticsAsync(95465499);

        Assert.Equal(zKillStatisticsOutcome.Failure, result.Outcome);
        Assert.Equal(1, client.RequestCount);
    }

    private sealed class CountingLimiter : IRequestStartLimiter
    {
        public int Calls { get; private set; }

        public long TotalWaitMilliseconds => 0;

        public bool IsPaused => false;

        public void Pause(TimeSpan duration)
        {
        }

        public Task WaitAsync(CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.CompletedTask;
        }
    }

    private sealed class CancellingLimiter : IRequestStartLimiter
    {
        public long TotalWaitMilliseconds => 0;

        public bool IsPaused => false;

        public void Pause(TimeSpan duration)
        {
        }

        public Task WaitAsync(CancellationToken cancellationToken = default) => Task.FromCanceled(new CancellationToken(true));
    }
}

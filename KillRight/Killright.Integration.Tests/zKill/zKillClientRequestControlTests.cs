using System.Net;
using System.Net.Http.Headers;
using Killright.Integration.RateLimiting;
using Killright.Integration.zKill;
using Killright.Shared.zKill;
using Xunit;

namespace Killright.Integration.Tests.zKill;

public sealed class zKillClientRequestControlTests
{
    private const long Lukas = 2116955190;
    private const string StatisticsJson = """{"shipsDestroyed":1,"soloKills":0,"soloRatio":0,"avgGangSize":2,"shipsLost":0,"soloLosses":0}""";

    private static readonly zKillClientOptions FastTimeout = new() { RequestTimeout = TimeSpan.FromMilliseconds(50) };

    [Fact]
    public async Task GetStatisticsAsync_TimeoutThenSuccess_MakesTwoRequestsAndReturnsSuccess()
    {
        var handler = new SequenceHandler(
            Hang,
            (_, _) => Task.FromResult(Json(StatisticsJson)));
        var client = new zKillClient(new HttpClient(handler), FastTimeout);

        var result = await client.GetStatisticsAsync(Lukas);

        Assert.Equal(zKillStatisticsOutcome.Success, result.Outcome);
        Assert.Equal(2, handler.Calls);
        Assert.Equal(2, client.RequestCount);
        Assert.Equal(1, client.TimeoutCount);
        Assert.Equal(1, client.RetryCount);
    }

    [Fact]
    public async Task GetStatisticsAsync_TwoTimeoutsWithOneRetry_ReturnsFailureAfterTwoRequests()
    {
        var handler = new SequenceHandler(Hang, Hang, Hang);
        var client = new zKillClient(new HttpClient(handler), FastTimeout);

        var result = await client.GetStatisticsAsync(Lukas);

        Assert.Equal(zKillStatisticsOutcome.Failure, result.Outcome);
        Assert.Equal(2, handler.Calls);
        Assert.Equal(2, client.RequestCount);
        Assert.Equal(2, client.TimeoutCount);
        Assert.Equal(1, client.RetryCount);
    }

    [Fact]
    public async Task GetStatisticsAsync_RetryCountZero_DoesNotRetry()
    {
        var handler = new SequenceHandler(Hang, Hang);
        var client = new zKillClient(new HttpClient(handler), FastTimeout with { RetryCount = 0 });

        var result = await client.GetStatisticsAsync(Lukas);

        Assert.Equal(zKillStatisticsOutcome.Failure, result.Outcome);
        Assert.Equal(1, handler.Calls);
        Assert.Equal(0, client.RetryCount);
    }

    [Fact]
    public async Task GetStatisticsAsync_CallerCancellation_IsNotRetried()
    {
        var handler = new SequenceHandler(Hang, Hang);
        var client = new zKillClient(new HttpClient(handler), new zKillClientOptions { RequestTimeout = TimeSpan.FromSeconds(30) });
        using var cancellation = new CancellationTokenSource();

        var call = client.GetStatisticsAsync(Lukas, cancellation.Token);
        await handler.FirstCallStarted;
        cancellation.Cancel();
        var result = await call;

        Assert.Equal(zKillStatisticsOutcome.Failure, result.Outcome);
        Assert.Equal(1, handler.Calls);
        Assert.Equal(0, client.TimeoutCount);
        Assert.Equal(0, client.RetryCount);
    }

    [Fact]
    public async Task GetStatisticsAsync_ConnectionFailureThenSuccess_RetriesOnce()
    {
        var handler = new SequenceHandler(
            (_, _) => throw new HttpRequestException("connection reset"),
            (_, _) => Task.FromResult(Json(StatisticsJson)));
        var client = new zKillClient(new HttpClient(handler));

        var result = await client.GetStatisticsAsync(Lukas);

        Assert.Equal(zKillStatisticsOutcome.Success, result.Outcome);
        Assert.Equal(2, client.RequestCount);
        Assert.Equal(1, client.RetryCount);
        Assert.Equal(0, client.TimeoutCount);
    }

    [Fact]
    public async Task GetStatisticsAsync_ServerError_IsNotRetried()
    {
        var handler = new SequenceHandler(
            (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)),
            (_, _) => Task.FromResult(Json(StatisticsJson)));
        var client = new zKillClient(new HttpClient(handler));

        var result = await client.GetStatisticsAsync(Lukas);

        Assert.Equal(zKillStatisticsOutcome.Failure, result.Outcome);
        Assert.Equal(1, handler.Calls);
        Assert.Equal(0, client.RetryCount);
    }

    [Fact]
    public async Task GetStatisticsAsync_RetryAttempt_WaitsForAndCountsAgainstTheBudget()
    {
        var clock = new FakeClock();
        var budget = new RollingWindowRequestBudget(1, TimeSpan.FromSeconds(60), () => clock.Now, clock.DelayAsync);
        var handler = new SequenceHandler(
            (_, _) => throw new HttpRequestException("connection reset"),
            (_, _) => Task.FromResult(Json(StatisticsJson)));
        var client = new zKillClient(new HttpClient(handler), limiter: budget);

        var result = await client.GetStatisticsAsync(Lukas);

        Assert.Equal(zKillStatisticsOutcome.Success, result.Outcome);
        Assert.Equal([TimeSpan.FromSeconds(60)], clock.Delays);
        Assert.Equal(2, client.RequestCount);
    }

    [Fact]
    public async Task GetStatisticsAsync_429WithRetryAfterSeconds_PausesThatLongAndLogsOnce()
    {
        var clock = new FakeClock();
        var budget = new RollingWindowRequestBudget(10, TimeSpan.FromSeconds(60), () => clock.Now, clock.DelayAsync);
        var log = new List<string>();
        var handler = new SequenceHandler(
            (_, _) => Task.FromResult(TooManyRequests(TimeSpan.FromSeconds(30))),
            (_, _) => Task.FromResult(Json(StatisticsJson)));
        var client = new zKillClient(new HttpClient(handler), limiter: budget, log: log.Add);

        var result = await client.GetStatisticsAsync(Lukas);

        Assert.Equal(zKillStatisticsOutcome.Failure, result.Outcome);
        Assert.Equal(1, handler.Calls);
        Assert.Equal(1, client.RateLimitedCount);
        Assert.Equal(0, client.RetryCount);
        Assert.Equal(["zKill rate limited (429); paused 30 s"], log);
        Assert.True(budget.IsPaused);

        clock.Advance(TimeSpan.FromSeconds(29));
        Assert.True(budget.IsPaused);

        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.False(budget.IsPaused);
    }

    [Fact]
    public async Task GetStatisticsAsync_429WithoutRetryAfter_PausesForTheConfiguredPause()
    {
        var clock = new FakeClock();
        var budget = new RollingWindowRequestBudget(10, TimeSpan.FromSeconds(60), () => clock.Now, clock.DelayAsync);
        var log = new List<string>();
        var handler = new SequenceHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.TooManyRequests)));
        var options = new zKillClientOptions { RateLimitPause = TimeSpan.FromSeconds(45) };
        var client = new zKillClient(new HttpClient(handler), options, budget, log.Add);

        await client.GetStatisticsAsync(Lukas);

        Assert.Equal(["zKill rate limited (429); paused 45 s"], log);

        clock.Advance(TimeSpan.FromSeconds(44));
        Assert.True(budget.IsPaused);

        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.False(budget.IsPaused);
    }

    [Fact]
    public async Task GetStatisticsAsync_429WithRetryAfterDate_PausesUntilThatDate()
    {
        var clock = new FakeClock();
        var budget = new RollingWindowRequestBudget(10, TimeSpan.FromSeconds(60), () => clock.Now, clock.DelayAsync);
        var log = new List<string>();
        var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        response.Headers.RetryAfter = new RetryConditionHeaderValue(DateTimeOffset.UtcNow.AddSeconds(90));
        var handler = new SequenceHandler((_, _) => Task.FromResult(response));
        var client = new zKillClient(new HttpClient(handler), limiter: budget, log: log.Add);

        await client.GetStatisticsAsync(Lukas);

        var message = Assert.Single(log);
        Assert.Matches(@"^zKill rate limited \(429\); paused (89|90) s$", message);
    }

    [Fact]
    public async Task Calls_DuringPause_SendNothingAndReturnFailure()
    {
        var clock = new FakeClock();
        var budget = new RollingWindowRequestBudget(10, TimeSpan.FromSeconds(60), () => clock.Now, clock.DelayAsync);
        var handler = new SequenceHandler(
            (_, _) => Task.FromResult(TooManyRequests(TimeSpan.FromSeconds(30))),
            (_, _) => Task.FromResult(Json(StatisticsJson)));
        var client = new zKillClient(new HttpClient(handler), limiter: budget);

        await client.GetStatisticsAsync(Lukas);
        var statistics = await client.GetStatisticsAsync(Lukas);
        var recent = await client.GetRecentKillmailsAsync(Lukas, 3600);
        var last = await client.GetLastKillmailAsync(Lukas);

        Assert.Equal(zKillStatisticsOutcome.Failure, statistics.Outcome);
        Assert.Equal(zKillRecentKillmailOutcome.Failure, recent.Outcome);
        Assert.Equal(zKillLastKillmailOutcome.Failure, last.Outcome);
        Assert.Equal(1, handler.Calls);
        Assert.Equal(1, client.RequestCount);
        Assert.Equal(3, client.PausedRejectCount);

        clock.Advance(TimeSpan.FromSeconds(30));
        var afterPause = await client.GetStatisticsAsync(Lukas);

        Assert.Equal(zKillStatisticsOutcome.Success, afterPause.Outcome);
        Assert.Equal(2, handler.Calls);
    }

    [Fact]
    public async Task Counters_StartAtZeroAndTrackEachEvent()
    {
        var handler = new SequenceHandler(
            Hang,
            (_, _) => Task.FromResult(Json(StatisticsJson)),
            (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.TooManyRequests)));
        var client = new zKillClient(new HttpClient(handler), FastTimeout);

        Assert.Equal(0, client.TimeoutCount + client.RetryCount + client.RateLimitedCount + client.PausedRejectCount + client.RequestCount);

        await client.GetStatisticsAsync(Lukas);
        await client.GetStatisticsAsync(Lukas);

        Assert.Equal(3, client.RequestCount);
        Assert.Equal(1, client.TimeoutCount);
        Assert.Equal(1, client.RetryCount);
        Assert.Equal(1, client.RateLimitedCount);
        Assert.Equal(0, client.PausedRejectCount);
    }

    private static async Task<HttpResponseMessage> Hang(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        await Task.Delay(Timeout.Infinite, cancellationToken);

        throw new InvalidOperationException("unreachable");
    }

    private static HttpResponseMessage Json(string body)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json")
        };
    }

    private static HttpResponseMessage TooManyRequests(TimeSpan retryAfter)
    {
        var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        response.Headers.RetryAfter = new RetryConditionHeaderValue(retryAfter);
        return response;
    }

    private sealed class SequenceHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>[] _steps;
        private readonly TaskCompletionSource _firstCallStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _calls;

        public SequenceHandler(params Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>[] steps)
        {
            _steps = steps;
        }

        public int Calls => Volatile.Read(ref _calls);

        public Task FirstCallStarted => _firstCallStarted.Task;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var index = Interlocked.Increment(ref _calls) - 1;
            _firstCallStarted.TrySetResult();

            return _steps[Math.Min(index, _steps.Length - 1)](request, cancellationToken);
        }
    }

    private sealed class FakeClock
    {
        public long Now { get; private set; }

        public List<TimeSpan> Delays { get; } = [];

        public void Advance(TimeSpan span) => Now += span.Ticks;

        public Task DelayAsync(TimeSpan span, CancellationToken cancellationToken)
        {
            Delays.Add(span);
            Advance(span);
            return Task.CompletedTask;
        }
    }
}

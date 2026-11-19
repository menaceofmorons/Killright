using System.Net;
using Killright.Integration.Esi;
using Killright.Integration.Sde;
using Killright.Integration.Tests.Esi;
using Killright.Integration.zKill;
using Killright.Shared.Sde;
using Killright.Shared.zKill;
using Xunit;

namespace Killright.Integration.Tests;

public sealed class ClientFailureCauseTests
{
    private const long Lukas = 2116955190;
    private const string BadJson = "{ not json";

    private static CancellationToken Cancelled()
    {
        var source = new CancellationTokenSource();
        source.Cancel();
        return source.Token;
    }

    public static TheoryData<string> ZkillFailureKinds => new() { "timeout-exhausted", "connection", "server-error", "unreadable" };

    [Theory]
    [MemberData(nameof(ZkillFailureKinds))]
    public async Task zKill_FailureKind_ReturnsFailureAndLogsCause(string kind)
    {
        var log = new List<string>();
        var scripted = new ScriptedHttpMessageHandler();
        var options = new zKillClientOptions { RequestTimeout = TimeSpan.FromMilliseconds(50), RetryCount = 0 };
        HttpMessageHandler handler = scripted;

        switch (kind)
        {
            case "timeout-exhausted":
                handler = new HangingHandler();
                break;
            case "connection":
                scripted.ThrowOnUriContaining("api/", new HttpRequestException("reset"));
                break;
            case "server-error":
                scripted.OnUriContaining("api/", HttpStatusCode.InternalServerError);
                break;
            default:
                scripted.OnUriContaining("api/", HttpStatusCode.OK, BadJson);
                break;
        }

        var client = new zKillClient(new HttpClient(handler), options, log: log.Add);

        var result = await client.GetStatisticsAsync(Lukas);

        Assert.Equal(zKillStatisticsOutcome.Failure, result.Outcome);
        var entry = Assert.Single(log);
        Assert.Contains("failed", entry);
        Assert.Contains(kind switch
        {
            "timeout-exhausted" => "timed out",
            "connection" => "reset",
            "server-error" => "HTTP 500",
            _ => "unreadable response"
        }, entry);
    }

    [Fact]
    public async Task zKill_CancelledToken_PropagatesOnEveryCall()
    {
        var client = new zKillClient(new HttpClient(new HangingHandler()));
        var token = Cancelled();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.GetStatisticsAsync(Lukas, token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.GetRecentKillmailsAsync(Lukas, 3600, token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.GetLastKillmailAsync(Lukas, token));
    }

    [Fact]
    public async Task zKill_OutcomesStaySeparate()
    {
        var empty = new ScriptedHttpMessageHandler().OnUriContaining("api/", HttpStatusCode.OK, "[]");
        var invalid = new ScriptedHttpMessageHandler().OnUriContaining("api/", HttpStatusCode.OK, """{"error":"Invalid type or id"}""");

        var emptyResult = await new zKillClient(new HttpClient(empty)).GetRecentKillmailsAsync(Lukas, 3600);
        var invalidResult = await new zKillClient(new HttpClient(invalid)).GetRecentKillmailsAsync(Lukas, 3600);

        Assert.Equal(zKillRecentKillmailOutcome.Success, emptyResult.Outcome);
        Assert.Empty(emptyResult.RawKillmails);
        Assert.Equal(zKillRecentKillmailOutcome.NoHistory, invalidResult.Outcome);
    }

    [Theory]
    [InlineData("connection", "reset")]
    [InlineData("server-error", "HTTP 503")]
    [InlineData("unreadable", "JsonException")]
    public async Task Esi_FailureKind_ReturnsNullAndLogsCause(string kind, string expected)
    {
        var log = new List<string>();
        var handler = new ScriptedHttpMessageHandler();

        switch (kind)
        {
            case "connection":
                handler.ThrowOnUriContaining("characters/", new HttpRequestException("reset"));
                break;
            case "server-error":
                handler.OnUriContaining("characters/", HttpStatusCode.ServiceUnavailable);
                break;
            default:
                handler.OnUriContaining("characters/", HttpStatusCode.OK, BadJson);
                break;
        }

        var client = new EsiClient(new HttpClient(handler), log: log.Add);

        var details = await client.GetCharacterDetailsAsync(1);

        Assert.Null(details);
        var entry = Assert.Single(log);
        Assert.Contains(expected, entry);
    }

    [Fact]
    public async Task Esi_Timeout_ReturnsFailedLookupAndLogsTimeout()
    {
        var log = new List<string>();
        var http = new HttpClient(new HangingHandler()) { Timeout = TimeSpan.FromMilliseconds(50) };
        var client = new EsiClient(http, log: log.Add);

        var result = await client.ResolveNamesAsync(["Lukas Naarii"]);

        Assert.Equal(EsiNameLookup.Failed, result["Lukas Naarii"]);
        Assert.Contains("timed out", Assert.Single(log));
    }

    [Fact]
    public async Task Esi_CancelledToken_PropagatesOnEveryCallPath()
    {
        var handler = new ScriptedHttpMessageHandler().OnUriContaining("/", HttpStatusCode.OK, "{}");
        var client = new EsiClient(new HttpClient(handler));
        var token = Cancelled();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.GetCharacterDetailsAsync(1, token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.ResolveNamesAsync(["a"], token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.GetAffiliationsAsync([1], token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.GetEntityNamesAsync([1], token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.ResolvePilotAsync("a", token));
    }

    [Fact]
    public async Task Sde_ServerError_ReturnsFailureAndLogsCause()
    {
        var log = new List<string>();
        var handler = new ScriptedHttpMessageHandler().OnUriContaining("latest", HttpStatusCode.BadGateway);

        var result = await new SdeClient(new HttpClient(handler), Options(), log.Add).GetManifestAsync();

        Assert.Equal(SdeManifestOutcome.Failure, result.Outcome);
        Assert.Contains("HTTP 502", Assert.Single(log));
    }

    [Fact]
    public async Task Sde_ConnectionFailure_LogsCauseForManifestAndDownload()
    {
        var log = new List<string>();
        var handler = new ScriptedHttpMessageHandler().ThrowOnUriContaining("latest", new HttpRequestException("reset"));
        var client = new SdeClient(new HttpClient(handler), Options(), log.Add);
        var path = Path.Combine(Path.GetTempPath(), $"sde-{Guid.NewGuid():N}.zip");

        var manifest = await client.GetManifestAsync();
        var download = await client.DownloadDatasetZipAsync(path);

        Assert.Equal(SdeManifestOutcome.Failure, manifest.Outcome);
        Assert.Equal(SdeDatasetDownloadOutcome.Failure, download.Outcome);
        Assert.Equal(2, log.Count);
        Assert.All(log, entry => Assert.Contains("reset", entry));
    }

    [Fact]
    public async Task Sde_CancelledToken_Propagates()
    {
        var handler = new ScriptedHttpMessageHandler().OnUriContaining("latest", HttpStatusCode.OK, "{}");
        var client = new SdeClient(new HttpClient(handler), Options());
        var token = Cancelled();
        var path = Path.Combine(Path.GetTempPath(), $"sde-{Guid.NewGuid():N}.zip");

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.GetManifestAsync(token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.DownloadDatasetZipAsync(path, null, token));
    }

    private static SdeClientOptions Options() => new()
    {
        ManifestUrl = "https://example.test/latest.jsonl",
        DatasetZipUrl = "https://example.test/latest.zip"
    };

    private sealed class HangingHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            throw new InvalidOperationException("unreachable");
        }
    }
}

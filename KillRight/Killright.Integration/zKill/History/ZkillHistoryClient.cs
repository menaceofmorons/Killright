using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;

namespace Killright.Integration.zKill.History;

public sealed class ZkillHistoryClient : IZkillHistoryClient
{
    private const string HistoryEndpointFormat = "https://r2z2.zkillboard.com/history/raw/{0}.json";
    private static readonly TimeSpan RequestSpacing = TimeSpan.FromSeconds(1);

    private readonly HttpClient _httpClient;

    public ZkillHistoryClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<ZkillHistoryMonthResult> CountPreviousCompleteMonthAsync(CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var todayUtc = DateTime.UtcNow.Date;
        var firstDayOfCurrentMonth = new DateTime(todayUtc.Year, todayUtc.Month, 1);
        var firstDayOfTargetMonth = firstDayOfCurrentMonth.AddMonths(-1);
        var daysInTargetMonth = DateTime.DaysInMonth(firstDayOfTargetMonth.Year, firstDayOfTargetMonth.Month);

        var startDate = DateOnly.FromDateTime(firstDayOfTargetMonth);
        var endDate = DateOnly.FromDateTime(firstDayOfTargetMonth.AddDays(daysInTargetMonth - 1));
        var results = new List<ZkillHistoryDayResult>();

        for (var date = startDate; date <= endDate; date = date.AddDays(1))
        {
            cancellationToken.ThrowIfCancellationRequested();
            results.Add(await CountDayAsync(date, cancellationToken));

            if (date < endDate)
                await Task.Delay(RequestSpacing, cancellationToken);
        }

        stopwatch.Stop();

        return new ZkillHistoryMonthResult
        {
            StartDate = startDate,
            EndDate = endDate,
            Elapsed = stopwatch.Elapsed,
            Days = results
        };
    }

    private async Task<ZkillHistoryDayResult> CountDayAsync(DateOnly date, CancellationToken cancellationToken)
    {
        var dateText = date.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        var url = string.Format(CultureInfo.InvariantCulture, HistoryEndpointFormat, dateText);

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.UserAgent.Add(new ProductInfoHeaderValue("KillRight", "19.00.01"));
            request.Headers.AcceptEncoding.Add(new StringWithQualityHeaderValue("gzip"));
            request.Headers.AcceptEncoding.Add(new StringWithQualityHeaderValue("deflate"));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var content = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return new ZkillHistoryDayResult(
                    date,
                    url,
                    false,
                    0,
                    0,
                    0,
                    $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}");
            }

            using var document = JsonDocument.Parse(content);
            var metrics = CountDayMetrics(document.RootElement);

            return new ZkillHistoryDayResult(
                date,
                url,
                true,
                metrics.KillmailCount,
                metrics.AttackerCount,
                metrics.MaxAttackersOnKillmail,
                null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new ZkillHistoryDayResult(
                date,
                url,
                false,
                0,
                0,
                0,
                ex.Message);
        }
    }

    private static ZkillHistoryDayMetrics CountDayMetrics(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
            return new ZkillHistoryDayMetrics(0, 0, 0);

        var killmailCount = 0;
        var attackerCount = 0;
        var maxAttackersOnKillmail = 0;

        foreach (var killmailProperty in root.EnumerateObject())
        {
            killmailCount++;
            var killmail = killmailProperty.Value;

            if (killmail.ValueKind != JsonValueKind.Object)
                continue;

            if (!killmail.TryGetProperty("attackers", out var attackers))
                continue;

            if (attackers.ValueKind != JsonValueKind.Array)
                continue;

            var attackersForKillmail = attackers.GetArrayLength();
            attackerCount += attackersForKillmail;
            maxAttackersOnKillmail = Math.Max(maxAttackersOnKillmail, attackersForKillmail);
        }

        return new ZkillHistoryDayMetrics(killmailCount, attackerCount, maxAttackersOnKillmail);
    }

    private sealed record ZkillHistoryDayMetrics(
        int KillmailCount,
        int AttackerCount,
        int MaxAttackersOnKillmail);
}
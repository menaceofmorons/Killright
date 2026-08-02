using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;

namespace Killright.Integration.zKill.History;

public sealed class ZkillHistoryClient : IZkillHistoryClient
{
    private const string HistoryEndpointFormat = "https://zkillboard.com/api/history/{0}.json";
    private static readonly TimeSpan RequestSpacing = TimeSpan.FromSeconds(1);

    private readonly HttpClient _httpClient;

    public ZkillHistoryClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<ZkillHistoryMonthResult> CountPreviousCompleteMonthAsync(CancellationToken cancellationToken = default)
    {
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

        return new ZkillHistoryMonthResult
        {
            StartDate = startDate,
            EndDate = endDate,
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
            request.Headers.UserAgent.Add(new ProductInfoHeaderValue("KillRight", "19.00.00"));
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
                    $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}");
            }

            using var document = JsonDocument.Parse(content);
            var rowCount = CountRows(document.RootElement);

            return new ZkillHistoryDayResult(
                date,
                url,
                true,
                rowCount,
                null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new ZkillHistoryDayResult(
                date,
                url,
                false,
                0,
                ex.Message);
        }
    }

    private static int CountRows(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Array)
            return root.GetArrayLength();

        if (root.ValueKind != JsonValueKind.Object)
            return 0;

        if (root.TryGetProperty("killmails", out var killmails) && killmails.ValueKind == JsonValueKind.Array)
            return killmails.GetArrayLength();

        if (root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
            return data.GetArrayLength();

        return root.EnumerateObject().Count();
    }
}
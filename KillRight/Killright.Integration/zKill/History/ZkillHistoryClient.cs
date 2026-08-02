using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;

namespace Killright.Integration.zKill.History;

public sealed class ZkillHistoryClient : IZkillHistoryClient
{
    private const string HistoryEndpointFormat = "https://r2z2.zkillboard.com/history/raw/{0}.json";
    private const int MinimumQualifyingAttackers = 2;
    private const int FleetMinimumAttackers = 11;

    private static readonly TimeSpan RequestSpacing = TimeSpan.FromSeconds(1);
    private static readonly HashSet<long> PodShipTypeIds = new() { 670 };

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
        var uniqueQualifyingPilots = new HashSet<long>();
        var uniqueCandidateRelationships = new HashSet<PilotPair>();

        for (var date = startDate; date <= endDate; date = date.AddDays(1))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var processingResult = await CountDayAsync(date, cancellationToken);
            results.Add(processingResult.DayResult);

            foreach (var pilotId in processingResult.UniqueQualifyingPilots)
                uniqueQualifyingPilots.Add(pilotId);

            foreach (var pair in processingResult.UniqueCandidateRelationships)
                uniqueCandidateRelationships.Add(pair);

            if (date < endDate)
                await Task.Delay(RequestSpacing, cancellationToken);
        }

        stopwatch.Stop();

        return new ZkillHistoryMonthResult
        {
            StartDate = startDate,
            EndDate = endDate,
            Elapsed = stopwatch.Elapsed,
            Days = results,
            UniqueQualifyingPilots = uniqueQualifyingPilots.Count,
            UniqueCandidateRelationships = uniqueCandidateRelationships.Count
        };
    }

    private async Task<ZkillHistoryDayProcessingResult> CountDayAsync(DateOnly date, CancellationToken cancellationToken)
    {
        var dateText = date.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        var url = string.Format(CultureInfo.InvariantCulture, HistoryEndpointFormat, dateText);

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.UserAgent.Add(new ProductInfoHeaderValue("KillRight", "19.00.02"));
            request.Headers.AcceptEncoding.Add(new StringWithQualityHeaderValue("gzip"));
            request.Headers.AcceptEncoding.Add(new StringWithQualityHeaderValue("deflate"));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var content = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var failedDay = new ZkillHistoryDayResult(
                    date,
                    url,
                    false,
                    0,
                    0,
                    0,
                    0,
                    0,
                    0,
                    0,
                    0,
                    0,
                    $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}");

                return new ZkillHistoryDayProcessingResult(
                    failedDay,
                    new HashSet<long>(),
                    new HashSet<PilotPair>());
            }

            using var document = JsonDocument.Parse(content);
            return CountDayMetrics(date, url, document.RootElement);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var failedDay = new ZkillHistoryDayResult(
                date,
                url,
                false,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                ex.Message);

            return new ZkillHistoryDayProcessingResult(
                failedDay,
                new HashSet<long>(),
                new HashSet<PilotPair>());
        }
    }

    private static ZkillHistoryDayProcessingResult CountDayMetrics(DateOnly date, string url, JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            var invalidDay = new ZkillHistoryDayResult(
                date,
                url,
                false,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                "Root JSON was not an object.");

            return new ZkillHistoryDayProcessingResult(
                invalidDay,
                new HashSet<long>(),
                new HashSet<PilotPair>());
        }

        var rawKillmailCount = 0;
        var rawAttackerCount = 0;
        var podKillmailCount = 0;
        var insufficientAttackerKillmailCount = 0;
        var fleetKillmailCount = 0;
        var qualifyingKillmailCount = 0;
        var qualifyingAttackerCount = 0;
        var candidatePairOccurrenceRows = 0L;
        var maxQualifyingAttackersOnKillmail = 0;
        var uniqueQualifyingPilots = new HashSet<long>();
        var uniqueCandidateRelationships = new HashSet<PilotPair>();

        foreach (var killmailProperty in root.EnumerateObject())
        {
            rawKillmailCount++;
            var killmail = killmailProperty.Value;

            if (killmail.ValueKind != JsonValueKind.Object)
                continue;

            if (!killmail.TryGetProperty("attackers", out var attackers) || attackers.ValueKind != JsonValueKind.Array)
                continue;

            rawAttackerCount += attackers.GetArrayLength();

            if (IsPodKillmail(killmail))
            {
                podKillmailCount++;
                continue;
            }

            var attackerCharacterIds = GetUniqueAttackerCharacterIds(attackers);

            if (attackerCharacterIds.Count < MinimumQualifyingAttackers)
            {
                insufficientAttackerKillmailCount++;
                continue;
            }

            if (attackerCharacterIds.Count >= FleetMinimumAttackers)
            {
                fleetKillmailCount++;
                continue;
            }

            qualifyingKillmailCount++;
            qualifyingAttackerCount += attackerCharacterIds.Count;
            candidatePairOccurrenceRows += CountPairs(attackerCharacterIds.Count);
            maxQualifyingAttackersOnKillmail = Math.Max(maxQualifyingAttackersOnKillmail, attackerCharacterIds.Count);

            foreach (var characterId in attackerCharacterIds)
                uniqueQualifyingPilots.Add(characterId);

            AddCandidatePairs(attackerCharacterIds, uniqueCandidateRelationships);
        }

        var dayResult = new ZkillHistoryDayResult(
            date,
            url,
            true,
            rawKillmailCount,
            rawAttackerCount,
            podKillmailCount,
            insufficientAttackerKillmailCount,
            fleetKillmailCount,
            qualifyingKillmailCount,
            qualifyingAttackerCount,
            candidatePairOccurrenceRows,
            maxQualifyingAttackersOnKillmail,
            null);

        return new ZkillHistoryDayProcessingResult(
            dayResult,
            uniqueQualifyingPilots,
            uniqueCandidateRelationships);
    }

    private static bool IsPodKillmail(JsonElement killmail)
    {
        if (!killmail.TryGetProperty("victim", out var victim) || victim.ValueKind != JsonValueKind.Object)
            return false;

        if (!victim.TryGetProperty("ship_type_id", out var shipTypeIdElement))
            return false;

        return shipTypeIdElement.TryGetInt64(out var shipTypeId) && PodShipTypeIds.Contains(shipTypeId);
    }

    private static List<long> GetUniqueAttackerCharacterIds(JsonElement attackers)
    {
        var values = new HashSet<long>();

        foreach (var attacker in attackers.EnumerateArray())
        {
            if (attacker.ValueKind != JsonValueKind.Object)
                continue;

            if (!attacker.TryGetProperty("character_id", out var characterIdElement))
                continue;

            if (!characterIdElement.TryGetInt64(out var characterId))
                continue;

            if (characterId <= 0)
                continue;

            values.Add(characterId);
        }

        var result = values.ToList();
        result.Sort();
        return result;
    }

    private static long CountPairs(int participantCount)
    {
        return participantCount < 2 ? 0 : participantCount * (long)(participantCount - 1) / 2;
    }

    private static void AddCandidatePairs(IReadOnlyList<long> attackerCharacterIds, HashSet<PilotPair> uniqueCandidateRelationships)
    {
        for (var outerIndex = 0; outerIndex < attackerCharacterIds.Count - 1; outerIndex++)
        {
            for (var innerIndex = outerIndex + 1; innerIndex < attackerCharacterIds.Count; innerIndex++)
            {
                uniqueCandidateRelationships.Add(new PilotPair(
                    attackerCharacterIds[outerIndex],
                    attackerCharacterIds[innerIndex]));
            }
        }
    }

    private sealed record ZkillHistoryDayProcessingResult(
        ZkillHistoryDayResult DayResult,
        HashSet<long> UniqueQualifyingPilots,
        HashSet<PilotPair> UniqueCandidateRelationships);

    private readonly record struct PilotPair(long PilotA, long PilotB);
}
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
    private const int MaxParallelWorkers = 8;
    private const int MaxRequestsPerSecond = 10;

    private static readonly DateOnly CalendarYearStartDate = new(2025, 1, 1);
    private static readonly DateOnly CalendarYearEndDate = new(2025, 12, 31);
    private static readonly HashSet<long> PodShipTypeIds = new() { 670 };

    private readonly HttpClient _httpClient;

    public ZkillHistoryClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<ZkillHistoryDayResult> CountDayAsync(DateOnly date, CancellationToken cancellationToken = default)
    {
        var processingResult = await CountDayCoreAsync(date, null, false, cancellationToken);
        return processingResult.DayResult;
    }

    public async Task<ZkillHistoryEvidenceDayResult> ExtractDayEvidenceAsync(DateOnly date, CancellationToken cancellationToken = default)
    {
        var processingResult = await CountDayCoreAsync(date, null, true, cancellationToken);
        return new ZkillHistoryEvidenceDayResult(
            processingResult.DayResult,
            processingResult.EvidenceRows,
            processingResult.ParticipantRows);
    }

    public async Task<ZkillHistoryPeriodResult> CountCalendarYear2025Async(CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var dates = GetDateRange(CalendarYearStartDate, CalendarYearEndDate);
        var rateLimiter = new AsyncRequestRateLimiter(MaxRequestsPerSecond);
        using var workerLimiter = new SemaphoreSlim(MaxParallelWorkers, MaxParallelWorkers);

        var tasks = dates.Select(async date =>
        {
            await workerLimiter.WaitAsync(cancellationToken);

            try
            {
                return await CountDayCoreAsync(date, rateLimiter, false, cancellationToken);
            }
            finally
            {
                workerLimiter.Release();
            }
        });

        var dayProcessingResults = await Task.WhenAll(tasks);
        var orderedProcessingResults = dayProcessingResults.OrderBy(x => x.DayResult.Date).ToList();
        var dayResults = orderedProcessingResults.Select(x => x.DayResult).ToList();
        var uniqueQualifyingPilots = new HashSet<long>();
        var relationshipOccurrenceCounts = new Dictionary<PilotPair, int>();

        foreach (var processingResult in orderedProcessingResults)
        {
            foreach (var pilotId in processingResult.UniqueQualifyingPilots)
                uniqueQualifyingPilots.Add(pilotId);

            foreach (var pairCount in processingResult.CandidateRelationshipCounts)
            {
                relationshipOccurrenceCounts.TryGetValue(pairCount.Key, out var currentCount);
                relationshipOccurrenceCounts[pairCount.Key] = currentCount + pairCount.Value;
            }
        }

        stopwatch.Stop();

        return new ZkillHistoryPeriodResult
        {
            ReportTitle = "KillRight Group Detection Calendar Year 2025 Pilot",
            StartDate = CalendarYearStartDate,
            EndDate = CalendarYearEndDate,
            Elapsed = stopwatch.Elapsed,
            Days = dayResults,
            UniqueQualifyingPilots = uniqueQualifyingPilots.Count,
            UniqueCandidateRelationships = relationshipOccurrenceCounts.Count,
            RelationshipFrequencyDistribution = RelationshipFrequencyDistribution.FromCounts(relationshipOccurrenceCounts.Values)
        };
    }

    private async Task<ZkillHistoryDayProcessingResult> CountDayCoreAsync(
        DateOnly date,
        AsyncRequestRateLimiter? rateLimiter,
        bool includeRows,
        CancellationToken cancellationToken)
    {
        var dateText = date.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        var url = string.Format(CultureInfo.InvariantCulture, HistoryEndpointFormat, dateText);

        try
        {
            if (rateLimiter is not null)
                await rateLimiter.WaitAsync(cancellationToken);

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.UserAgent.Add(new ProductInfoHeaderValue("KillRight", "19.00.33"));
            request.Headers.AcceptEncoding.Add(new StringWithQualityHeaderValue("gzip"));
            request.Headers.AcceptEncoding.Add(new StringWithQualityHeaderValue("deflate"));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var content = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var failedDay = CreateFailedDay(date, url, $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}");
                return ZkillHistoryDayProcessingResult.Failed(failedDay);
            }

            using var document = JsonDocument.Parse(content);
            return CountDayMetrics(date, url, document.RootElement, includeRows);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var failedDay = CreateFailedDay(date, url, ex.Message);
            return ZkillHistoryDayProcessingResult.Failed(failedDay);
        }
    }

    private static ZkillHistoryDayProcessingResult CountDayMetrics(DateOnly date, string url, JsonElement root, bool includeRows)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            var invalidDay = CreateFailedDay(date, url, "Root JSON was not an object.");
            return ZkillHistoryDayProcessingResult.Failed(invalidDay);
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
        var candidateRelationshipCounts = new Dictionary<PilotPair, int>();
        var evidenceRows = includeRows ? new List<ZkillHistoryEvidenceRecord>() : new List<ZkillHistoryEvidenceRecord>(0);
        var participantRows = includeRows ? new List<ZkillHistoryParticipantRecord>() : new List<ZkillHistoryParticipantRecord>(0);

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

            var attackersByCharacterId = GetUniqueAttackers(attackers);
            var attackerCharacterIds = attackersByCharacterId.Keys.OrderBy(x => x).ToArray();

            if (attackerCharacterIds.Length < MinimumQualifyingAttackers)
            {
                insufficientAttackerKillmailCount++;
                continue;
            }

            if (attackerCharacterIds.Length >= FleetMinimumAttackers)
            {
                fleetKillmailCount++;
                continue;
            }

            var killmailId = GetLong(killmail, "killmail_id") ?? long.Parse(killmailProperty.Name, CultureInfo.InvariantCulture);
            var killmailTimeUtc = GetDateTime(killmail, "killmail_time") ?? date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

            qualifyingKillmailCount++;
            qualifyingAttackerCount += attackerCharacterIds.Length;
            candidatePairOccurrenceRows += CountPairs(attackerCharacterIds.Length);
            maxQualifyingAttackersOnKillmail = Math.Max(maxQualifyingAttackersOnKillmail, attackerCharacterIds.Length);

            foreach (var characterId in attackerCharacterIds)
                uniqueQualifyingPilots.Add(characterId);

            AddCandidatePairs(attackerCharacterIds, candidateRelationshipCounts);

            if (includeRows)
            {
                evidenceRows.Add(new ZkillHistoryEvidenceRecord(
                    killmailId,
                    killmailTimeUtc,
                    date,
                    GetLong(killmail, "solar_system_id"),
                    attackerCharacterIds.Length));

                foreach (var characterId in attackerCharacterIds)
                {
                    var attacker = attackersByCharacterId[characterId];
                    participantRows.Add(new ZkillHistoryParticipantRecord(
                        killmailId,
                        characterId,
                        attacker.CorporationId,
                        attacker.AllianceId,
                        attacker.ShipTypeId));
                }
            }
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

        return new ZkillHistoryDayProcessingResult(dayResult, uniqueQualifyingPilots, candidateRelationshipCounts, evidenceRows, participantRows);
    }

    private static ZkillHistoryDayResult CreateFailedDay(DateOnly date, string url, string errorMessage)
    {
        return new ZkillHistoryDayResult(date, url, false, 0, 0, 0, 0, 0, 0, 0, 0, 0, errorMessage);
    }

    private static bool IsPodKillmail(JsonElement killmail)
    {
        if (!killmail.TryGetProperty("victim", out var victim) || victim.ValueKind != JsonValueKind.Object)
            return false;

        if (!victim.TryGetProperty("ship_type_id", out var shipTypeIdElement))
            return false;

        return shipTypeIdElement.TryGetInt64(out var shipTypeId) && PodShipTypeIds.Contains(shipTypeId);
    }

    private static Dictionary<long, AttackerSnapshot> GetUniqueAttackers(JsonElement attackers)
    {
        var values = new Dictionary<long, AttackerSnapshot>();

        foreach (var attacker in attackers.EnumerateArray())
        {
            if (attacker.ValueKind != JsonValueKind.Object)
                continue;

            var characterId = GetLong(attacker, "character_id");

            if (characterId is null || characterId.Value <= 0)
                continue;

            values[characterId.Value] = new AttackerSnapshot(
                GetLong(attacker, "corporation_id"),
                GetLong(attacker, "alliance_id"),
                GetLong(attacker, "ship_type_id"));
        }

        return values;
    }

    private static long CountPairs(int participantCount)
    {
        return participantCount < 2 ? 0 : participantCount * (long)(participantCount - 1) / 2;
    }

    private static List<DateOnly> GetDateRange(DateOnly startDate, DateOnly endDate)
    {
        var dates = new List<DateOnly>();

        for (var date = startDate; date <= endDate; date = date.AddDays(1))
            dates.Add(date);

        return dates;
    }

    private static void AddCandidatePairs(IReadOnlyList<long> attackerCharacterIds, Dictionary<PilotPair, int> candidateRelationshipCounts)
    {
        for (var outerIndex = 0; outerIndex < attackerCharacterIds.Count - 1; outerIndex++)
        {
            for (var innerIndex = outerIndex + 1; innerIndex < attackerCharacterIds.Count; innerIndex++)
            {
                var pair = new PilotPair(attackerCharacterIds[outerIndex], attackerCharacterIds[innerIndex]);
                candidateRelationshipCounts.TryGetValue(pair, out var currentCount);
                candidateRelationshipCounts[pair] = currentCount + 1;
            }
        }
    }

    private static long? GetLong(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value))
            return null;

        return value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var result) ? result : null;
    }

    private static DateTime? GetDateTime(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.String)
            return null;

        return DateTime.TryParse(
            value.GetString(),
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var result)
                ? result
                : null;
    }

    private sealed class AsyncRequestRateLimiter
    {
        private readonly SemaphoreSlim _gate = new(1, 1);
        private readonly TimeSpan _minimumSpacing;
        private DateTimeOffset _nextAllowedUtc = DateTimeOffset.MinValue;

        public AsyncRequestRateLimiter(int maxRequestsPerSecond)
        {
            if (maxRequestsPerSecond <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxRequestsPerSecond));

            _minimumSpacing = TimeSpan.FromSeconds(1d / maxRequestsPerSecond);
        }

        public async Task WaitAsync(CancellationToken cancellationToken)
        {
            await _gate.WaitAsync(cancellationToken);

            try
            {
                var now = DateTimeOffset.UtcNow;

                if (_nextAllowedUtc > now)
                {
                    var delay = _nextAllowedUtc - now;
                    await Task.Delay(delay, cancellationToken);
                    now = DateTimeOffset.UtcNow;
                }

                _nextAllowedUtc = now + _minimumSpacing;
            }
            finally
            {
                _gate.Release();
            }
        }
    }

    private sealed record ZkillHistoryDayProcessingResult(
        ZkillHistoryDayResult DayResult,
        HashSet<long> UniqueQualifyingPilots,
        Dictionary<PilotPair, int> CandidateRelationshipCounts,
        IReadOnlyList<ZkillHistoryEvidenceRecord> EvidenceRows,
        IReadOnlyList<ZkillHistoryParticipantRecord> ParticipantRows)
    {
        public static ZkillHistoryDayProcessingResult Failed(ZkillHistoryDayResult dayResult)
        {
            return new ZkillHistoryDayProcessingResult(
                dayResult,
                new HashSet<long>(),
                new Dictionary<PilotPair, int>(),
                Array.Empty<ZkillHistoryEvidenceRecord>(),
                Array.Empty<ZkillHistoryParticipantRecord>());
        }
    }

    private sealed record AttackerSnapshot(long? CorporationId, long? AllianceId, long? ShipTypeId);

    private readonly record struct PilotPair(long PilotA, long PilotB);
}
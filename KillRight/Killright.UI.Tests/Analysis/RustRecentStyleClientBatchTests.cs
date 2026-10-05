using Killright.Shared.zKill;
using Killright.Storage.Diagnostics;
using Killright.UI.Analysis;
using Xunit;

namespace Killright.UI.Tests.Analysis;

public sealed class RustRecentStyleClientBatchTests
{
    private const long Lukas = 95465499;
    private const long Tral = 91321792;
    private const long Symptom = 2112625428;

    private const string Success =
        "{\"results\":["
        + "{\"character_id\":95465499,\"recent_style\":\"Solo\",\"is_recent_podder\":true,\"threat\":{\"score\":30},"
        + "\"derived_activity\":{\"has_public_activity_data\":true,\"kills_week\":3,\"solo_week\":1,"
        + "\"info_week_losses\":4,"
        + "\"newest_non_pod_killmail\":{\"kill_time_utc\":\"2026-09-28T10:00:00.0000000Z\",\"activity_type\":\"Loss\"},"
        + "\"newest_non_pod_kill_time_utc\":\"2026-09-27T08:30:00+00:00\"}},"
        + "{\"character_id\":91321792,\"recent_style\":\"Gang\",\"is_recent_podder\":false,\"threat\":{\"score\":0},"
        + "\"derived_activity\":{\"has_public_activity_data\":false}}"
        + "],\"timings_ms\":{\"batch_total\":12.5,\"killmails_query\":3.0},\"timing_counts\":{\"pilots_analyzed\":2,\"pilot_failures\":0}}";

    [Fact]
    public async Task AnalyzePilotsAsync_Success_MapsEachPilotInRequestOrder()
    {
        var client = new RustRecentStyleClient(new FakeRuntime(Success));

        var results = await client.AnalyzePilotsAsync([Tral, Lukas]);

        Assert.Equal(2, results.Count);
        Assert.Equal("Gang", results[0].RecentStyle.ToString());
        Assert.False(results[0].IsRecentPodder);
        Assert.Equal("Solo", results[1].RecentStyle.ToString());
        Assert.True(results[1].IsRecentPodder);
        Assert.All(results, result => Assert.Null(result.FailureReason));
    }

    [Fact]
    public async Task AnalyzePilotsAsync_Success_MapsDerivedActivity()
    {
        var client = new RustRecentStyleClient(new FakeRuntime(Success));

        var results = await client.AnalyzePilotsAsync([Lukas, Tral]);

        var derived = results[0].DerivedActivity!;
        Assert.True(derived.HasPublicActivityData);
        Assert.Equal(3, derived.KillsWeek);
        Assert.Equal(1, derived.SoloWeek);
        Assert.Equal(4, derived.InfoWeekLosses);
        Assert.Equal(zKillActivityType.Loss, derived.NewestKillActivityType);
        Assert.Equal(new DateTimeOffset(2026, 9, 28, 10, 0, 0, TimeSpan.Zero), derived.NewestKillTimeUtc);
        Assert.Equal(new DateTimeOffset(2026, 9, 27, 8, 30, 0, TimeSpan.Zero), derived.LastKillUtc);

        var activity = derived.ToActivity(Lukas, new DateTimeOffset(2026, 9, 30, 0, 0, 0, TimeSpan.Zero));
        Assert.Equal(Lukas, activity.CharacterId);
        Assert.Equal(derived.NewestKillTimeUtc, activity.LastActiveUtc);
        Assert.Equal(zKillActivityType.Loss, activity.LastActivityType);
        Assert.Equal(derived.LastKillUtc, activity.LastKillUtc);

        var none = results[1].DerivedActivity!;
        Assert.False(none.HasPublicActivityData);
        Assert.Null(none.KillsWeek);
        Assert.Null(none.InfoWeekLosses);
        Assert.Null(none.NewestKillTimeUtc);
    }

    [Fact]
    public async Task AnalyzePilotsAsync_PerPilotFailure_FailsOnlyThatPilot()
    {
        var response =
            "{\"results\":["
            + "{\"character_id\":95465499,\"recent_style\":\"Solo\",\"is_recent_podder\":false,\"threat\":{\"score\":30}},"
            + "{\"character_id\":91321792,\"failure\":\"repository_read_error:killmails\"}"
            + "]}";
        var client = new RustRecentStyleClient(new FakeRuntime(response));

        var results = await client.AnalyzePilotsAsync([Lukas, Tral]);

        Assert.Null(results[0].FailureReason);
        Assert.Equal("repository_read_error:killmails", results[1].FailureReason);
        Assert.Equal("Unk", results[1].ThreatBand);
        Assert.Equal("Unknown", results[1].RecentStyle.ToString());
    }

    [Fact]
    public async Task AnalyzePilotsAsync_WholeCallFailure_FailsEveryPilotWithTheReason()
    {
        var client = new RustRecentStyleClient(new FakeRuntime("{\"results\":[],\"failure\":\"missing_runtime\"}"));

        var results = await client.AnalyzePilotsAsync([Lukas, Tral, Symptom]);

        Assert.Equal(3, results.Count);
        Assert.All(results, result => Assert.Equal("missing_runtime", result.FailureReason));
    }

    [Fact]
    public async Task AnalyzePilotsAsync_PilotMissingFromResponse_IsFailed()
    {
        var client = new RustRecentStyleClient(new FakeRuntime(Success));

        var results = await client.AnalyzePilotsAsync([Lukas, Symptom]);

        Assert.Null(results[0].FailureReason);
        Assert.Equal("missing_result", results[1].FailureReason);
    }

    [Fact]
    public async Task AnalyzePilotsAsync_RuntimeThrows_FailsEveryPilot()
    {
        var client = new RustRecentStyleClient(new FakeRuntime(null));

        var results = await client.AnalyzePilotsAsync([Lukas, Tral]);

        Assert.All(results, result => Assert.Equal("exception", result.FailureReason));
    }

    [Fact]
    public async Task AnalyzePilotsAsync_MalformedJson_FailsEveryPilot()
    {
        var client = new RustRecentStyleClient(new FakeRuntime("not json"));

        var results = await client.AnalyzePilotsAsync([Lukas]);

        Assert.Equal("exception", Assert.Single(results).FailureReason);
    }

    [Fact]
    public async Task AnalyzePilotsAsync_EmptyList_ReturnsEmptyWithoutCallingTheEngine()
    {
        var runtime = new FakeRuntime(Success);
        var client = new RustRecentStyleClient(runtime);

        var results = await client.AnalyzePilotsAsync([]);

        Assert.Empty(results);
        Assert.Equal(0, runtime.BatchCalls);
    }

    [Fact]
    public async Task AnalyzePilotsAsync_SendsOneRequestCarryingEveryCharacterId()
    {
        var runtime = new FakeRuntime(Success);
        var client = new RustRecentStyleClient(runtime);

        await client.AnalyzePilotsAsync([Lukas, Tral, Symptom]);

        Assert.Equal(1, runtime.BatchCalls);
        Assert.Equal("{\"character_ids\":[95465499,91321792,2112625428]}", runtime.LastRequest);
    }

    [Fact]
    public async Task AnalyzePilotsAsync_WithTiming_RecordsBatchEngineRowsAndCounters()
    {
        var timings = new ScanTimings();
        var client = new RustRecentStyleClient(new FakeRuntime(Success));

        await client.AnalyzePilotsAsync([Lukas, Tral], timings: timings);

        Assert.Contains(timings.Rows, row => row.Level == ScanTimings.EngineLevel && row.Phase == "batch_total" && row.CharacterId is null);
        Assert.Contains(timings.Rows, row => row.Phase == "count_pilots_analyzed" && row.Milliseconds == 2);
        Assert.Contains(timings.Rows, row => row.Phase == "json_serialize");
        Assert.Contains(timings.Rows, row => row.Phase == "json_deserialize");
        Assert.Contains(timings.Rows, row => row.Phase == "native_call");
    }

    [Fact]
    public async Task AnalyzePilotsAsync_ResponseWithoutTiming_RecordsNoEngineRows()
    {
        var timings = new ScanTimings();
        var client = new RustRecentStyleClient(new FakeRuntime("{\"results\":[{\"character_id\":95465499,\"recent_style\":\"Solo\",\"is_recent_podder\":false,\"threat\":{\"score\":30}}]}"));

        await client.AnalyzePilotsAsync([Lukas], timings: timings);

        Assert.DoesNotContain(timings.Rows, row => row.Phase == "batch_total");
    }

    private sealed class FakeRuntime : IKillrightEngineRuntime
    {
        private readonly string? _batchResponse;

        public FakeRuntime(string? batchResponse)
        {
            _batchResponse = batchResponse;
        }

        public int BatchCalls { get; private set; }

        public string? LastRequest { get; private set; }

        public bool IsAvailable => true;

        public Task<string> AnalyzePilotAsync(
            string requestJson,
            CancellationToken cancellationToken = default,
            ScanTimings? timings = null,
            long? timingCharacterId = null)
            => throw new NotSupportedException();

        public Task<string> AnalyzePilotsAsync(
            string requestJson,
            CancellationToken cancellationToken = default,
            ScanTimings? timings = null)
        {
            BatchCalls++;
            LastRequest = requestJson;

            if (_batchResponse is null)
                throw new InvalidOperationException("engine unavailable");

            using (timings.Measure(ScanTimings.EngineLevel, "native_call"))
            {
                return Task.FromResult(_batchResponse);
            }
        }

        public Task<string> DiagnoseGroupDetectionAsync(string requestJson, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<string> DiagnoseThreatAsync(string requestJson, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public void Dispose()
        {
        }
    }
}

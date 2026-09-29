using Killright.Storage.Diagnostics;
using Killright.UI.Analysis;
using Xunit;

namespace Killright.UI.Tests.Analysis;

public sealed class RustRecentStyleClientTimingTests
{
    private const string PilotResponseWithoutTiming =
        "{\"character_id\":95465499,\"recent_style\":\"Solo\",\"is_recent_podder\":false,\"threat\":{\"score\":30}}";

    private const string PilotResponseWithTiming =
        "{\"character_id\":95465499,\"recent_style\":\"Solo\",\"is_recent_podder\":false,\"threat\":{\"score\":30},"
        + "\"timings_ms\":{\"pilot_total\":4.5,\"killmails_query\":2.25},\"timing_counts\":{\"killmail_rows\":12}}";

    private const string GroupResponseWithTiming =
        "{\"character_id\":95465499,\"group_detection\":{\"relationships\":[]},"
        + "\"timings_ms\":{\"group_total\":10.0},\"timing_counts\":{\"scanned_pilots\":2}}";

    [Fact]
    public async Task AnalyzeAsync_ResponseWithoutTiming_ReturnsNoTimingsAndRecordsNoEngineRows()
    {
        var timings = new ScanTimings();
        var client = new RustRecentStyleClient(new FakeRuntime(PilotResponseWithoutTiming));

        var result = await client.AnalyzeAsync(95465499, timings: timings);

        Assert.Null(result.Timings);
        Assert.DoesNotContain(timings.Rows, row => row.Phase == "pilot_total");
        Assert.Contains(timings.Rows, row => row.Phase == "json_serialize" && row.CharacterId == 95465499);
        Assert.Contains(timings.Rows, row => row.Phase == "json_deserialize" && row.CharacterId == 95465499);
    }

    [Fact]
    public async Task AnalyzeAsync_ResponseWithTiming_MapsObjectsAndRecordsEngineRows()
    {
        var timings = new ScanTimings();
        var client = new RustRecentStyleClient(new FakeRuntime(PilotResponseWithTiming));

        var result = await client.AnalyzeAsync(95465499, timings: timings);

        Assert.NotNull(result.Timings);
        Assert.Equal(4.5, result.Timings!.TimingsMs["pilot_total"]);
        Assert.Equal(12, result.Timings.TimingCounts["killmail_rows"]);
        Assert.Contains(timings.Rows, row => row.Level == ScanTimings.EngineLevel && row.Phase == "killmails_query" && row.CharacterId == 95465499);
        Assert.Contains(timings.Rows, row => row.Phase == "count_killmail_rows" && row.Milliseconds == 12);
    }

    [Fact]
    public async Task AnalyzeAsync_NullSession_StillMapsTimingsWithoutThrowing()
    {
        var client = new RustRecentStyleClient(new FakeRuntime(PilotResponseWithTiming));

        var result = await client.AnalyzeAsync(95465499);

        Assert.Equal("Solo", result.RecentStyle.ToString());
        Assert.NotNull(result.Timings);
    }

    [Fact]
    public async Task AnalyzeGroupAsync_ResponseWithTiming_MapsObjectsAndRecordsGroupRows()
    {
        var timings = new ScanTimings();
        var client = new RustRecentStyleClient(new FakeRuntime(GroupResponseWithTiming));

        var result = await client.AnalyzeGroupAsync(new long[] { 95465499, 2112625428 }, timings: timings);

        Assert.NotNull(result.Timings);
        Assert.Equal(10.0, result.Timings!.TimingsMs["group_total"]);
        Assert.Contains(timings.Rows, row => row.Phase == "group_total" && row.CharacterId is null);
        Assert.Contains(timings.Rows, row => row.Phase == "relationship_map");
    }

    [Fact]
    public async Task AnalyzeGroupAsync_ResponseWithoutTiming_ReturnsNoTimings()
    {
        var client = new RustRecentStyleClient(new FakeRuntime("{\"character_id\":1,\"group_detection\":{\"relationships\":[]}}"));

        var result = await client.AnalyzeGroupAsync(new long[] { 1, 2 });

        Assert.Null(result.Timings);
    }

    private sealed class FakeRuntime : IKillrightEngineRuntime
    {
        private readonly string _response;

        public FakeRuntime(string response)
        {
            _response = response;
        }

        public bool IsAvailable => true;

        public Task<string> AnalyzePilotAsync(
            string requestJson,
            CancellationToken cancellationToken = default,
            ScanTimings? timings = null,
            long? timingCharacterId = null)
        {
            using (timings.Measure(ScanTimings.EngineLevel, "native_call", timingCharacterId))
            {
                return Task.FromResult(_response);
            }
        }

        public Task<string> DiagnoseGroupDetectionAsync(string requestJson, CancellationToken cancellationToken = default)
            => Task.FromResult(_response);

        public Task<string> DiagnoseThreatAsync(string requestJson, CancellationToken cancellationToken = default)
            => Task.FromResult(_response);

        public void Dispose()
        {
        }
    }
}

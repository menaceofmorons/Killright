using System.Text.Json;
using Killright.Storage.Diagnostics;
using Killright.UI.Analysis;
using Killright.UI.Diagnostics;
using Xunit;

namespace Killright.UI.Tests.Analysis;

public sealed class EngineRequestInputTests
{
    private const long Lukas = 95465499;
    private const long Tral = 91321792;

    private const string PilotResponse =
        "{\"character_id\":95465499,\"recent_style\":\"Solo\",\"is_recent_podder\":false,\"threat\":{\"score\":30}}";

    private const string GroupResponse =
        "{\"character_id\":95465499,\"group_detection\":{\"relationships\":[]}}";

    private const string ThreatDiagnosticsResponse =
        "{\"character_id\":95465499,\"diagnostics\":{\"historical_capability\":10,\"survivability\":5,\"loss_quality\":4,"
        + "\"recent_activity_modifier\":2.0,\"security_modifier\":0,\"score\":21,\"coverage_start_present\":true,"
        + "\"observed_days\":14.0,\"counted_kills\":3,\"daily_rate\":0.21,\"gate_rule\":\"none\",\"floor_applied\":false}}";

    private const string GroupDiagnosticsResponse =
        "{\"character_id\":95465499,\"diagnostics\":{\"direct_relationships\":[],\"chains\":[],\"hubs\":[],"
        + "\"direct_analysis_duration_ms\":1,\"chain_analysis_duration_ms\":2}}";

    [Fact]
    public async Task AnalyzeAsync_RequestCarriesThePilotInputsRead()
    {
        var runtime = new CapturingRuntime(PilotResponse);
        var client = new RustRecentStyleClient(runtime, new FakeEngineInputReader());

        var result = await client.AnalyzeAsync(Lukas);

        Assert.Null(result.FailureReason);
        Assert.Equal(1, runtime.Calls);

        using var request = JsonDocument.Parse(runtime.LastRequest!);
        Assert.Equal(Lukas, request.RootElement.GetProperty("character_id").GetInt64());
        Assert.Equal(Lukas, request.RootElement.GetProperty("pilot").GetProperty("character_id").GetInt64());
        Assert.Equal(1, request.RootElement.GetProperty("pilot").GetProperty("killmails").GetArrayLength());
        Assert.Equal(JsonValueKind.Null, request.RootElement.GetProperty("group_inputs").ValueKind);
    }

    [Fact]
    public async Task AnalyzeAsync_ReaderFailure_FailsWithTheReasonWithoutCallingTheEngine()
    {
        var runtime = new CapturingRuntime(PilotResponse);
        var reader = new FakeEngineInputReader(new Dictionary<long, string> { [Lukas] = "repository_read_error:identity" });
        var client = new RustRecentStyleClient(runtime, reader);

        var result = await client.AnalyzeAsync(Lukas);

        Assert.Equal("repository_read_error:identity", result.FailureReason);
        Assert.Equal(0, runtime.Calls);
    }

    [Fact]
    public async Task AnalyzeGroupAsync_RequestCarriesTheGroupInputsRead()
    {
        var runtime = new CapturingRuntime(GroupResponse);
        var client = new RustRecentStyleClient(runtime, new FakeEngineInputReader());

        await client.AnalyzeGroupAsync([Lukas, Tral]);

        Assert.Equal(1, runtime.Calls);

        using var request = JsonDocument.Parse(runtime.LastRequest!);
        var inputs = request.RootElement.GetProperty("group_inputs");
        Assert.Equal(2, request.RootElement.GetProperty("scanned_character_ids").GetArrayLength());
        Assert.Equal(1, inputs.GetProperty("direct_evidence").GetArrayLength());
        Assert.Equal(0, inputs.GetProperty("chain_evidence").GetArrayLength());
        Assert.Equal(1000001, inputs.GetProperty("npc_corporation_ids")[0].GetInt64());
        Assert.Equal(JsonValueKind.Null, request.RootElement.GetProperty("pilot").ValueKind);
    }

    [Fact]
    public async Task AnalyzeGroupAsync_ReaderFailure_ReturnsEmptyWithoutCallingTheEngine()
    {
        var runtime = new CapturingRuntime(GroupResponse);
        var client = new RustRecentStyleClient(runtime, new FakeEngineInputReader(groupFailure: "repository_read_error:chain_evidence"));

        var result = await client.AnalyzeGroupAsync([Lukas, Tral]);

        Assert.Empty(result.Relationships);
        Assert.Equal(0, runtime.Calls);
    }

    [Fact]
    public async Task DiagnoseGroupDetectionAsync_RequestCarriesTheGroupInputsRead()
    {
        var runtime = new CapturingRuntime(GroupDiagnosticsResponse);
        var client = new EngineDiagnosticsClient(runtime, new FakeEngineInputReader());

        var result = await client.DiagnoseGroupDetectionAsync([Lukas, Tral]);

        Assert.Equal(1, runtime.Calls);
        Assert.Equal(2, result.ChainAnalysisDurationMs);

        using var request = JsonDocument.Parse(runtime.LastRequest!);
        Assert.Equal(1, request.RootElement.GetProperty("group_inputs").GetProperty("direct_evidence").GetArrayLength());
        Assert.Equal(2, request.RootElement.GetProperty("scanned_character_ids").GetArrayLength());
    }

    [Fact]
    public async Task DiagnoseGroupDetectionAsync_ReaderFailure_ReturnsEmptyWithoutCallingTheEngine()
    {
        var runtime = new CapturingRuntime(GroupDiagnosticsResponse);
        var client = new EngineDiagnosticsClient(runtime, new FakeEngineInputReader(groupFailure: "repository_read_error:direct_evidence"));

        var result = await client.DiagnoseGroupDetectionAsync([Lukas, Tral]);

        Assert.Same(GroupDetectionDiagnosticsResult.Empty, result);
        Assert.Equal(0, runtime.Calls);
    }

    [Fact]
    public async Task DiagnoseThreatAsync_RequestCarriesThePilotInputsRead()
    {
        var runtime = new CapturingRuntime(ThreatDiagnosticsResponse);
        var client = new EngineDiagnosticsClient(runtime, new FakeEngineInputReader());

        var table = await client.DiagnoseThreatAsync(Lukas);

        Assert.Equal(1, runtime.Calls);
        Assert.Contains(table.Rows.Cast<System.Data.DataRow>(), row => (string)row["Component"] == "Score" && (string)row["Value"] == "21");

        using var request = JsonDocument.Parse(runtime.LastRequest!);
        Assert.Equal(Lukas, request.RootElement.GetProperty("pilot").GetProperty("character_id").GetInt64());
        Assert.Equal("Gang", request.RootElement.GetProperty("pilot").GetProperty("statistics").GetProperty("general_style").GetString());
    }

    [Fact]
    public async Task DiagnoseThreatAsync_ReaderFailure_ShowsTheReasonWithoutCallingTheEngine()
    {
        var runtime = new CapturingRuntime(ThreatDiagnosticsResponse);
        var reader = new FakeEngineInputReader(new Dictionary<long, string> { [Lukas] = "repository_read_error:statistics" });
        var client = new EngineDiagnosticsClient(runtime, reader);

        var table = await client.DiagnoseThreatAsync(Lukas);

        var row = Assert.Single(table.Rows.Cast<System.Data.DataRow>());
        Assert.Equal("Failure", (string)row["Component"]);
        Assert.Equal("repository_read_error:statistics", (string)row["Value"]);
        Assert.Equal(0, runtime.Calls);
    }

    private sealed class CapturingRuntime : IKillrightEngineRuntime
    {
        private readonly string _response;

        public CapturingRuntime(string response)
        {
            _response = response;
        }

        public int Calls { get; private set; }

        public string? LastRequest { get; private set; }

        public bool IsAvailable => true;

        public Task<string> AnalyzePilotAsync(
            string requestJson,
            CancellationToken cancellationToken = default,
            ScanTimings? timings = null,
            long? timingCharacterId = null)
            => Capture(requestJson);

        public Task<string> DiagnoseGroupDetectionAsync(string requestJson, CancellationToken cancellationToken = default)
            => Capture(requestJson);

        public Task<string> DiagnoseThreatAsync(string requestJson, CancellationToken cancellationToken = default)
            => Capture(requestJson);

        public void Dispose()
        {
        }

        private Task<string> Capture(string requestJson)
        {
            Calls++;
            LastRequest = requestJson;

            return Task.FromResult(_response);
        }
    }
}

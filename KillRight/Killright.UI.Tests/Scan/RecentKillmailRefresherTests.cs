using Killright.Integration.zKill;
using Killright.Shared.Killmails;
using Killright.Shared.zKill;
using Killright.Storage.Scan;
using Killright.UI.Scan;
using Xunit;

namespace Killright.UI.Tests.Scan;

public sealed class RecentKillmailRefresherTests
{
    private const long Lukas = 95465499;
    private static readonly DateTimeOffset Now = new(2026, 9, 30, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Refresh_PilotWithTheNoHistoryMarker_MakesNoCallAndCountsAsSuccessfulCheck()
    {
        var client = new FakeClient(zKillRecentKillmailOutcome.Success);
        var writes = new ScanWriteBatch();

        var outcome = await Refresh(client, new zKillStatistics { NoHistory = true }, writes);

        Assert.Equal(0, client.Calls);
        Assert.False(outcome.Failed);
        Assert.Equal(Now, outcome.NewLastSuccessfulCallUtc);
        Assert.Null(outcome.PastSecondsRequested);
        Assert.True(writes.IsEmpty);
    }

    [Fact]
    public async Task Refresh_InvalidIdAnswer_WritesTheMarkerAndRecordsASuccessfulCheck()
    {
        var client = new FakeClient(zKillRecentKillmailOutcome.NoHistory);
        var writes = new ScanWriteBatch();

        var outcome = await Refresh(client, new zKillStatistics(), writes);

        Assert.Equal(1, client.Calls);
        Assert.False(outcome.Failed);
        Assert.Equal(Now, outcome.NewLastSuccessfulCallUtc);
        Assert.Equal([Lukas], writes.NoHistoryMarks);
        Assert.Empty(writes.Statistics);
    }

    [Fact]
    public async Task Refresh_InvalidIdAnswerWithNoStatisticsRow_WritesNoStatisticsRow()
    {
        var client = new FakeClient(zKillRecentKillmailOutcome.NoHistory);
        var writes = new ScanWriteBatch();

        await Refresh(client, null, writes);

        Assert.Empty(writes.Statistics);
    }

    [Fact]
    public async Task Refresh_SuccessWithNone_WritesNoMarker()
    {
        var client = new FakeClient(zKillRecentKillmailOutcome.Success);
        var writes = new ScanWriteBatch();

        var outcome = await Refresh(client, new zKillStatistics(), writes);

        Assert.False(outcome.Failed);
        Assert.Equal(Now, outcome.NewLastSuccessfulCallUtc);
        Assert.Empty(writes.NoHistoryMarks);
        Assert.Empty(writes.NoHistoryClears);
        Assert.True(writes.IsEmpty);
    }

    [Fact]
    public async Task Refresh_FailureAnswer_WritesNothingAndRecordsNoSuccess()
    {
        var client = new FakeClient(zKillRecentKillmailOutcome.Failure);
        var writes = new ScanWriteBatch();

        var outcome = await Refresh(client, new zKillStatistics(), writes);

        Assert.True(outcome.Failed);
        Assert.Null(outcome.NewLastSuccessfulCallUtc);
        Assert.True(writes.IsEmpty);
    }

    [Fact]
    public async Task Refresh_PassesTheScanCancellationTokenToTheCall()
    {
        var client = new FakeClient(zKillRecentKillmailOutcome.Success);
        using var source = new CancellationTokenSource();

        await RecentKillmailRefresher.RefreshAsync(
            client, Lukas, 0, null, new zKillStatistics(), false, new ScanWriteBatch(), Now, null, source.Token);

        Assert.Equal(source.Token, client.Token);
    }

    [Fact]
    public async Task Refresh_CancelledScanToken_PropagatesCancellationInsteadOfFailing()
    {
        var client = new FakeClient(zKillRecentKillmailOutcome.Success) { ThrowOnCancelledToken = true };
        using var source = new CancellationTokenSource();
        await source.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => RecentKillmailRefresher.RefreshAsync(
            client, Lukas, 0, null, new zKillStatistics(), false, new ScanWriteBatch(), Now, null, source.Token));
    }

    private static Task<RecentRefreshOutcome> Refresh(FakeClient client, zKillStatistics? statistics, ScanWriteBatch writes)
    {
        return RecentKillmailRefresher.RefreshAsync(
            client, Lukas, 0, null, statistics, false, writes, Now, null, CancellationToken.None);
    }

    private sealed class FakeClient(zKillRecentKillmailOutcome outcome) : IzKillClient
    {
        public int Calls { get; private set; }

        public CancellationToken Token { get; private set; }

        public bool ThrowOnCancelledToken { get; init; }

        public long RequestCount => Calls;

        public Task<zKillRecentKillmailResult> GetRecentKillmailsAsync(long characterId, int pastSeconds, CancellationToken cancellationToken = default)
        {
            Calls++;
            Token = cancellationToken;
            if (ThrowOnCancelledToken)
                cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new zKillRecentKillmailResult(outcome, Array.Empty<RawKillmail>()));
        }

        public Task<zKillStatisticsResult> GetStatisticsAsync(long characterId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<zKillLastKillmailResult> GetLastKillmailAsync(long characterId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }
}

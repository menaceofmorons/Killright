namespace Killright.Integration.zKill.History;

public sealed record ZkillHistoryExtractionTiming(
    DateTime StartedUtc,
    DateTime CompletedUtc,
    TimeSpan TotalElapsed,
    TimeSpan DownloadElapsed,
    TimeSpan JsonParseElapsed,
    TimeSpan RowGenerationElapsed);
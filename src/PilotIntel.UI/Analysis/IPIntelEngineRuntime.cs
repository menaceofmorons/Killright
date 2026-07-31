namespace PilotIntel.UI.Analysis;

public interface IPIntelEngineRuntime : IDisposable
{
    bool IsAvailable { get; }

    Task<string> AnalyzePilotAsync(
        string requestJson,
        CancellationToken cancellationToken = default);
}
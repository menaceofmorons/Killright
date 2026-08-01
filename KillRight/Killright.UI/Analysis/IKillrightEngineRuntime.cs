namespace Killright.UI.Analysis;

public interface IKillrightEngineRuntime : IDisposable
{
    bool IsAvailable { get; }

    Task<string> AnalyzePilotAsync(
        string requestJson,
        CancellationToken cancellationToken = default);
}
namespace Killright.Storage.GroupHistory;

public static class GroupHistoryConstants
{
    public const int SchemaVersion = 1;
    public const int InitialHistoricImportHorizonYears = 10;
    public const int PromptThresholdMinutes = 1;
    public const int MaxR2RequestStartsPerSecond = 10;
    public const int MaxParallelWorkers = 8;
    public const int MinimumQualifyingAttackers = 2;
    public const int FleetMinimumAttackers = 11;
    public const long PodShipTypeId = 670;
    public const string HistoricDatabaseFileName = "KillRight.GroupHistory.duckdb";
}
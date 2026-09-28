using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Killright.UI.Configuration;

public static class ApplicationSettingsLoader
{
    private const int MinimumQualificationFleetThreshold = 3;
    private const int DefaultQualificationFleetThreshold = 11;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static string GetDefaultSettingsPath()
    {
        return Path.Combine(AppContext.BaseDirectory, "config", "settings.json");
    }

    public static ApplicationSettings LoadOrDefault(string? settingsPath = null)
    {
        var path = settingsPath ?? GetDefaultSettingsPath();

        try
        {
            if (!File.Exists(path))
                return new ApplicationSettings();

            var json = File.ReadAllText(path);
            var settings = JsonSerializer.Deserialize<ApplicationSettings>(json, JsonOptions) ?? new ApplicationSettings();
            return Normalize(settings);
        }
        catch
        {
            return new ApplicationSettings();
        }
    }

    private static ApplicationSettings Normalize(ApplicationSettings settings)
    {
        var qualificationFleetThreshold = settings.QualificationFleetThreshold >= MinimumQualificationFleetThreshold
            ? settings.QualificationFleetThreshold
            : DefaultQualificationFleetThreshold;

#if HISTORIC_RELATIONSHIPS
        var batchOptions = settings.GroupHistory.ToBatchOptions();
        var parallelOptions = settings.GroupHistory.ToParallelDownloadOptions();

        return new ApplicationSettings
        {
            RecentWindowDays = settings.RecentWindowDays,
            BackupFolder = settings.BackupFolder,
            BackupRotationCount = settings.BackupRotationCount,
            AlphaReleaseSchemaLocked = settings.AlphaReleaseSchemaLocked,
            ThreatBands = settings.ThreatBands,
            RelationshipConfidenceBands = settings.RelationshipConfidenceBands,
            HighlightOpacity = settings.HighlightOpacity,
            NpcCorporationIdThreshold = settings.NpcCorporationIdThreshold,
            QualificationFleetThreshold = qualificationFleetThreshold,
            Sde = settings.Sde,
            Threat = settings.Threat,
            GroupDetection = settings.GroupDetection,
            Style = settings.Style,
            GroupHistory = new GroupHistoryApplicationSettings
            {
                ImportBatchSize = batchOptions.EvidenceInsertBatchSize,
                ParallelDownloadWorkers = parallelOptions.ParallelDownloadWorkers,
                ZkillDocumentedMaxRequestsPerSecond = parallelOptions.ZkillDocumentedMaxRequestsPerSecond
            }
        };
#else
        return qualificationFleetThreshold == settings.QualificationFleetThreshold
            ? settings
            : new ApplicationSettings
            {
                RecentWindowDays = settings.RecentWindowDays,
                BackupFolder = settings.BackupFolder,
                BackupRotationCount = settings.BackupRotationCount,
                AlphaReleaseSchemaLocked = settings.AlphaReleaseSchemaLocked,
                ThreatBands = settings.ThreatBands,
                RelationshipConfidenceBands = settings.RelationshipConfidenceBands,
                HighlightOpacity = settings.HighlightOpacity,
                NpcCorporationIdThreshold = settings.NpcCorporationIdThreshold,
                QualificationFleetThreshold = qualificationFleetThreshold,
                Sde = settings.Sde,
                Threat = settings.Threat,
                GroupDetection = settings.GroupDetection,
                Style = settings.Style
            };
#endif
    }
}
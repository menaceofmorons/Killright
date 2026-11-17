using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Killright.UI.Configuration;

public sealed record SettingsLoadResult
{
    public ApplicationSettings? Settings { get; private init; }

    public string? Failure { get; private init; }

    public bool Succeeded => Settings is not null;

    public static SettingsLoadResult Success(ApplicationSettings settings) => new() { Settings = settings };

    public static SettingsLoadResult Failed(string failure) => new() { Failure = failure };
}

public static class ApplicationSettingsLoader
{
    private const int MinimumQualificationFleetThreshold = 3;
    private const int DefaultQualificationFleetThreshold = 11;
    private const int MinimumCount = 1;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static string GetDefaultSettingsPath()
    {
        return Path.Combine(AppContext.BaseDirectory, "config", "settings.json");
    }

    public static SettingsLoadResult Load(string? settingsPath = null)
    {
        var path = settingsPath ?? GetDefaultSettingsPath();

        string json;

        try
        {
            if (!File.Exists(path))
                return SettingsLoadResult.Failed($"The settings file was not found at '{path}'.");

            json = File.ReadAllText(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return SettingsLoadResult.Failed($"The settings file at '{path}' could not be read. {exception.Message}");
        }

        ApplicationSettings? settings;

        try
        {
            settings = JsonSerializer.Deserialize<ApplicationSettings>(json, JsonOptions);
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException)
        {
            return SettingsLoadResult.Failed($"The settings file at '{path}' is not valid. {exception.Message}");
        }

        if (settings is null)
            return SettingsLoadResult.Failed($"The settings file at '{path}' is empty.");

        var problem = Validate(settings);

        return problem is null
            ? SettingsLoadResult.Success(Normalize(settings))
            : SettingsLoadResult.Failed($"The settings file at '{path}' has an invalid value: {problem}.");
    }

    public static string? Validate(ApplicationSettings settings)
    {
        var nullSection = FirstNullSection(settings);

        if (nullSection is not null)
            return $"the {nullSection} section is null";

        var retryCount = (settings.Network.Zkill ?? new ZkillSettings()).RetryCount;

        return retryCount < MinimumCount
            ? $"network.zkill.retryCount is {retryCount}, below the minimum {MinimumCount}"
            : null;
    }

    private static string? FirstNullSection(ApplicationSettings settings)
    {
        var sections = new (string Name, object? Value)[]
        {
            ("threatBands", settings.ThreatBands),
            ("relationshipConfidenceBands", settings.RelationshipConfidenceBands),
            ("sde", settings.Sde),
            ("threat", settings.Threat),
            ("groupDetection", settings.GroupDetection),
            ("style", settings.Style),
            ("timing", settings.Timing),
            ("network", settings.Network),
            ("database", settings.Database)
        };

        return sections.FirstOrDefault(section => section.Value is null).Name;
    }

    private static ApplicationSettings Normalize(ApplicationSettings settings)
    {
        var qualificationFleetThreshold = settings.QualificationFleetThreshold >= MinimumQualificationFleetThreshold
            ? settings.QualificationFleetThreshold
            : DefaultQualificationFleetThreshold;

        var network = settings.Network.Normalized();
        var database = settings.Database.Normalized();

        return qualificationFleetThreshold == settings.QualificationFleetThreshold && network == settings.Network && database == settings.Database
            ? settings
            : new ApplicationSettings
            {
                RecentWindowDays = settings.RecentWindowDays,
                BackupFolder = settings.BackupFolder,
                BackupRotationCount = settings.BackupRotationCount,
                ThreatBands = settings.ThreatBands,
                RelationshipConfidenceBands = settings.RelationshipConfidenceBands,
                HighlightOpacity = settings.HighlightOpacity,
                QualificationFleetThreshold = qualificationFleetThreshold,
                Sde = settings.Sde,
                Threat = settings.Threat,
                GroupDetection = settings.GroupDetection,
                Style = settings.Style,
                Timing = settings.Timing,
                Network = network,
                Database = database
            };
    }
}

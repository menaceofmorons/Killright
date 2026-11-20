using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Killright.Storage.Database;

namespace Killright.UI.Configuration;

public enum SettingsFailureKind
{
    Unreadable,
    Invalid
}

public sealed record SettingsLoadResult
{
    public ApplicationSettings? Settings { get; private init; }

    public string? Failure { get; private init; }

    public SettingsFailureKind? FailureKind { get; private init; }

    public bool Succeeded => Settings is not null;

    public static SettingsLoadResult Success(ApplicationSettings settings) => new() { Settings = settings };

    public static SettingsLoadResult Failed(string failure, SettingsFailureKind kind = SettingsFailureKind.Invalid) => new() { Failure = failure, FailureKind = kind };
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

    public static SettingsLoadResult Load(string? settingsPath = null, Action<string>? log = null)
    {
        var path = settingsPath ?? GetDefaultSettingsPath();

        string json;

        try
        {
            if (!File.Exists(path))
                return SettingsLoadResult.Failed($"The settings file was not found at '{path}'.", SettingsFailureKind.Unreadable);

            json = File.ReadAllText(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return SettingsLoadResult.Failed($"The settings file at '{path}' could not be read. {exception.Message}", SettingsFailureKind.Unreadable);
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
            ? SettingsLoadResult.Success(Normalize(settings, json, log ?? (_ => { })))
            : SettingsLoadResult.Failed($"The settings file at '{path}' has an invalid value: {problem}.");
    }

    public static string? Validate(ApplicationSettings settings)
    {
        var nullSection = FirstNullSection(settings);

        if (nullSection is not null)
            return $"the {nullSection} section is null";

        var retryCount = (settings.Network?.Zkill ?? new ZkillSettings()).RetryCount;

        return retryCount < MinimumCount
            ? $"network.zkill.retryCount is {retryCount}, below the minimum {MinimumCount}"
            : null;
    }

    private static string? FirstNullSection(ApplicationSettings settings)
    {
        var sections = new (string Name, object? Value)[]
        {
            ("sde", settings.Sde),
            ("threat", settings.Threat),
            ("groupDetection", settings.GroupDetection),
            ("style", settings.Style),
            ("timing", settings.Timing)
        };

        return sections.FirstOrDefault(section => section.Value is null).Name;
    }

    private static ApplicationSettings Normalize(ApplicationSettings settings, string json, Action<string> log)
    {
        using (var document = JsonDocument.Parse(json))
            LogMissingSettings(document.RootElement, log);

        return new ApplicationSettings
        {
            RecentWindowDays = settings.RecentWindowDays,
            BackupFolder = settings.BackupFolder,
            BackupRotationCount = settings.BackupRotationCount,
            ThreatBands = ThreatBandSetting.ValidateOrDefault(settings.ThreatBands, log),
            RelationshipConfidenceBands = RelationshipConfidenceBandSetting.ValidateOrDefault(settings.RelationshipConfidenceBands, log),
            HighlightOpacity = settings.HighlightOpacity,
            QualificationFleetThreshold = SettingsAdjustment.Limit("qualificationFleetThreshold", settings.QualificationFleetThreshold, MinimumQualificationFleetThreshold, null, log),
            Sde = settings.Sde,
            Threat = settings.Threat,
            GroupDetection = settings.GroupDetection,
            Style = settings.Style,
            Timing = settings.Timing,
            Network = (settings.Network ?? new NetworkSettings()).Normalized(log),
            Database = (settings.Database ?? new DatabaseSettings()).Normalized(log),
            Window = (settings.Window ?? new WindowSettings()).Normalized(log)
        };
    }

    private static void LogMissingSettings(JsonElement root, Action<string> log)
    {
        foreach (var (path, defaultText) in DefaultedSettings)
        {
            if (!IsPresent(root, path.Split('.')))
                SettingsAdjustment.LogMissing(path, defaultText, log);
        }

        foreach (var bandsName in new[] { "threatBands", "relationshipConfidenceBands" })
        {
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty(bandsName, out _))
                log($"Setting '{bandsName}' read as missing; using the built-in bands.");
        }
    }

    private static bool IsPresent(JsonElement root, string[] path)
    {
        var current = root;

        foreach (var segment in path)
        {
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(segment, out current))
                return false;
        }

        return current.ValueKind != JsonValueKind.Null;
    }

    private static readonly (string Path, string DefaultText)[] DefaultedSettings =
    {
        ("qualificationFleetThreshold", DefaultQualificationFleetThreshold.ToString()),
        ("network.esiMaxConcurrency", NetworkSettings.DefaultEsiMaxConcurrency.ToString()),
        ("network.zkill.requestBudget", ZkillSettings.DefaultRequestBudget.ToString()),
        ("network.zkill.budgetWindowSeconds", ZkillSettings.DefaultBudgetWindowSeconds.ToString()),
        ("network.zkill.maxConcurrency", ZkillSettings.DefaultMaxConcurrency.ToString()),
        ("network.zkill.requestTimeoutSeconds", ZkillSettings.DefaultRequestTimeoutSeconds.ToString()),
        ("network.zkill.retryCount", ZkillSettings.DefaultRetryCount.ToString()),
        ("network.zkill.rateLimitPauseSeconds", ZkillSettings.DefaultRateLimitPauseSeconds.ToString()),
        ("database.busyTimeoutSeconds", KillRightDatabaseOptions.DefaultBusyTimeoutSeconds.ToString()),
        ("database.pageCacheMegabytes", KillRightDatabaseOptions.DefaultPageCacheMegabytes.ToString()),
        ("database.idleCheckpointSeconds", KillRightDatabaseOptions.DefaultIdleCheckpointSeconds.ToString()),
        ("window.f1.width", F1WindowSettings.DefaultWidth.ToString()),
        ("window.f1.height", F1WindowSettings.DefaultHeight.ToString())
    };
}

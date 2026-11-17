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
    public const int MinimumQualificationFleetThreshold = 3;

    public const int MinimumCount = 1;

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
        catch (JsonException exception)
        {
            return SettingsLoadResult.Failed($"The settings file at '{path}' is not valid. {exception.Message}");
        }

        if (settings is null)
            return SettingsLoadResult.Failed($"The settings file at '{path}' is empty.");

        var problems = Validate(settings);

        return problems.Count == 0
            ? SettingsLoadResult.Success(settings)
            : SettingsLoadResult.Failed($"The settings file at '{path}' has invalid values: {string.Join("; ", problems)}.");
    }

    public static IReadOnlyList<string> Validate(ApplicationSettings settings)
    {
        var problems = new List<string>();

        RequireAtLeast(problems, "qualificationFleetThreshold", settings.QualificationFleetThreshold, MinimumQualificationFleetThreshold);

        if (settings.Network is null || settings.Network.Zkill is null || settings.Database is null)
        {
            problems.Add("a network, network.zkill or database section is null");
            return problems;
        }

        var network = settings.Network;
        var zkill = network.Zkill;
        var database = settings.Database;

        RequireAtLeast(problems, "network.esiMaxConcurrency", network.EsiMaxConcurrency, MinimumCount);
        RequireAtLeast(problems, "network.zkill.requestBudget", zkill.RequestBudget, MinimumCount);

        if (zkill.RequestBudget > ZkillSettings.MaximumRequestBudget)
            problems.Add($"network.zkill.requestBudget is above {ZkillSettings.MaximumRequestBudget}");

        RequireAtLeast(problems, "network.zkill.budgetWindowSeconds", zkill.BudgetWindowSeconds, MinimumCount);
        RequireAtLeast(problems, "network.zkill.maxConcurrency", zkill.MaxConcurrency, MinimumCount);
        RequireAtLeast(problems, "network.zkill.requestTimeoutSeconds", zkill.RequestTimeoutSeconds, MinimumCount);
        RequireAtLeast(problems, "network.zkill.retryCount", zkill.RetryCount, MinimumCount);
        RequireAtLeast(problems, "network.zkill.rateLimitPauseSeconds", zkill.RateLimitPauseSeconds, MinimumCount);
        RequireAtLeast(problems, "database.busyTimeoutSeconds", database.BusyTimeoutSeconds, MinimumCount);
        RequireAtLeast(problems, "database.pageCacheMegabytes", database.PageCacheMegabytes, MinimumCount);
        RequireAtLeast(problems, "database.idleCheckpointSeconds", database.IdleCheckpointSeconds, MinimumCount);

        return problems;
    }

    private static void RequireAtLeast(List<string> problems, string name, int value, int minimum)
    {
        if (value < minimum)
            problems.Add($"{name} is {value}, below the minimum {minimum}");
    }
}

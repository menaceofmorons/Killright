using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Killright.Storage.GroupHistory.Models;

namespace Killright.UI.Configuration;

public static class ApplicationSettingsLoader
{
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
        var batchOptions = GroupHistoryImportBatchOptions.FromSingleBatchSize(settings.GroupHistory.ImportBatchSize);

        return new ApplicationSettings
        {
            GroupHistory = new GroupHistoryApplicationSettings
            {
                ImportBatchSize = batchOptions.EvidenceInsertBatchSize
            }
        };
    }
}
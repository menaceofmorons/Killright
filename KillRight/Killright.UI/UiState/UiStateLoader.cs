using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Killright.UI.UiState;

public sealed record UiStateLoadResult(UiStateModel State, bool FileExisted, bool WasCorrupt);

public static class UiStateLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public static string GetDefaultStatePath()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "KillRight",
            "ui-state.json");
    }

    public static UiStateLoadResult LoadOrDefault(string? statePath = null)
    {
        var path = statePath ?? GetDefaultStatePath();

        if (!File.Exists(path))
            return new UiStateLoadResult(new UiStateModel(), FileExisted: false, WasCorrupt: false);

        try
        {
            var json = File.ReadAllText(path);
            var state = JsonSerializer.Deserialize<UiStateModel>(json, JsonOptions);

            if (state is null)
                return new UiStateLoadResult(new UiStateModel(), FileExisted: true, WasCorrupt: true);

            return new UiStateLoadResult(state, FileExisted: true, WasCorrupt: false);
        }
        catch
        {
            return new UiStateLoadResult(new UiStateModel(), FileExisted: true, WasCorrupt: true);
        }
    }

    public static void Save(UiStateModel state, string? statePath = null)
    {
        var path = statePath ?? GetDefaultStatePath();
        var directory = Path.GetDirectoryName(path);

        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var json = JsonSerializer.Serialize(state, JsonOptions);
        File.WriteAllText(path, json);
    }
}

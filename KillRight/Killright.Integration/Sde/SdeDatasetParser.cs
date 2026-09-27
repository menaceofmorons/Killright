using System.IO.Compression;
using System.Text.Json;
using Killright.Shared.Sde;

namespace Killright.Integration.Sde;

public static class SdeDatasetParser
{
    private const string TypesEntryName = "types.jsonl";
    private const string SolarSystemsEntryName = "mapSolarSystems.jsonl";
    private const string NpcCorporationsEntryName = "npcCorporations.jsonl";

    public static SdeDatasetContents ParseZip(string zipPath)
    {
        using var archive = ZipFile.OpenRead(zipPath);

        var types = ParseTypes(RequireEntry(archive, TypesEntryName));
        var solarSystems = ParseSolarSystems(RequireEntry(archive, SolarSystemsEntryName));
        var npcCorporationIds = ParseNpcCorporationIds(RequireEntry(archive, NpcCorporationsEntryName));

        return new SdeDatasetContents(types, solarSystems, npcCorporationIds);
    }

    private static ZipArchiveEntry RequireEntry(ZipArchive archive, string entryName)
    {
        return archive.GetEntry(entryName)
            ?? throw new InvalidDataException($"SDE dataset zip is missing required entry '{entryName}'.");
    }

    private static List<SdeType> ParseTypes(ZipArchiveEntry entry)
    {
        var types = new List<SdeType>();

        foreach (var line in ReadJsonLines(entry))
        {
            if (!line.RootElement.TryGetProperty("published", out var publishedProperty)
                || publishedProperty.ValueKind != JsonValueKind.True)
                continue;

            if (!TryGetKeyAndEnglishName(line.RootElement, out var typeId, out var name))
                continue;

            types.Add(new SdeType(typeId, name));
        }

        return types;
    }

    private static List<SdeSolarSystem> ParseSolarSystems(ZipArchiveEntry entry)
    {
        var solarSystems = new List<SdeSolarSystem>();

        foreach (var line in ReadJsonLines(entry))
        {
            if (!TryGetKeyAndEnglishName(line.RootElement, out var systemId, out var name))
                continue;

            solarSystems.Add(new SdeSolarSystem(systemId, name));
        }

        return solarSystems;
    }

    private static List<long> ParseNpcCorporationIds(ZipArchiveEntry entry)
    {
        var corporationIds = new List<long>();

        foreach (var line in ReadJsonLines(entry))
        {
            if (!line.RootElement.TryGetProperty("_key", out var keyProperty))
                continue;

            corporationIds.Add(keyProperty.GetInt64());
        }

        return corporationIds;
    }

    private static bool TryGetKeyAndEnglishName(JsonElement root, out long key, out string name)
    {
        key = 0;
        name = string.Empty;

        if (!root.TryGetProperty("_key", out var keyProperty))
            return false;

        if (!root.TryGetProperty("name", out var nameProperty)
            || nameProperty.ValueKind != JsonValueKind.Object
            || !nameProperty.TryGetProperty("en", out var englishNameProperty)
            || englishNameProperty.ValueKind != JsonValueKind.String)
            return false;

        var englishName = englishNameProperty.GetString();

        if (string.IsNullOrEmpty(englishName))
            return false;

        key = keyProperty.GetInt64();
        name = englishName;
        return true;
    }

    private static IEnumerable<JsonDocument> ReadJsonLines(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        using var reader = new StreamReader(stream);

        string? line;

        while ((line = reader.ReadLine()) is not null)
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            JsonDocument document;

            try
            {
                document = JsonDocument.Parse(line);
            }
            catch (JsonException)
            {
                continue;
            }

            using (document)
            {
                yield return document;
            }
        }
    }
}

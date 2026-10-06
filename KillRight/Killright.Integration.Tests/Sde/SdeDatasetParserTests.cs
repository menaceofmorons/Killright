using System.IO.Compression;
using System.Text;
using Killright.Integration.Sde;
using Xunit;

namespace Killright.Integration.Tests.Sde;

public sealed class SdeDatasetParserTests
{
    [Fact]
    public void ParseZip_TypesEntry_KeepsOnlyPublishedRowsWithEnglishNames()
    {
        var zipPath = WriteZip(
            types: """
                {"_key": 587, "name": {"en": "Rifter", "de": "Rifter DE"}, "published": true}
                {"_key": 999, "name": {"en": "Unpublished Stub"}, "published": false}
                {"_key": 1000, "name": {"de": "No English Name"}, "published": true}
                """,
            solarSystems: "",
            npcCorporations: "");

        try
        {
            var contents = SdeDatasetParser.ParseZip(zipPath);

            Assert.Single(contents.Types);
            Assert.Equal(587, contents.Types[0].TypeId);
            Assert.Equal("Rifter", contents.Types[0].Name);
        }
        finally
        {
            File.Delete(zipPath);
        }
    }

    [Fact]
    public void ParseZip_SolarSystemsEntry_KeepsAllRowsWithEnglishNames()
    {
        var zipPath = WriteZip(
            types: "",
            solarSystems: """
                {"_key": 30000142, "name": {"en": "Jita"}}
                {"_key": 30000144, "name": {"en": "Perimeter"}}
                """,
            npcCorporations: "");

        try
        {
            var contents = SdeDatasetParser.ParseZip(zipPath);

            Assert.Equal(2, contents.SolarSystems.Count);
            Assert.Contains(contents.SolarSystems, s => s is { SystemId: 30000142, Name: "Jita" });
            Assert.Contains(contents.SolarSystems, s => s is { SystemId: 30000144, Name: "Perimeter" });
        }
        finally
        {
            File.Delete(zipPath);
        }
    }

    [Fact]
    public void ParseZip_NpcCorporationsEntry_KeepsRowsRegardlessOfDeletedFlag()
    {
        var zipPath = WriteZip(
            types: "",
            solarSystems: "",
            npcCorporations: """
                {"_key": 1000001, "deleted": false}
                {"_key": 1000132, "deleted": true}
                """);

        try
        {
            var contents = SdeDatasetParser.ParseZip(zipPath);

            Assert.Equal(new long[] { 1000001, 1000132 }, contents.NpcCorporationIds.OrderBy(id => id));
        }
        finally
        {
            File.Delete(zipPath);
        }
    }

    [Fact]
    public void ParseZip_FactionsEntry_KeepsRowsWithEnglishNames()
    {
        var zipPath = WriteZip(
            types: "",
            solarSystems: "",
            npcCorporations: "",
            factions: """
                {"_key": 500001, "name": {"en": "Caldari State", "de": "Staat der Caldari"}}
                {"_key": 500010, "name": {"en": "Guristas Pirates"}}
                {"_key": 500099, "name": {"de": "No English Name"}}
                """);

        try
        {
            var contents = SdeDatasetParser.ParseZip(zipPath);

            Assert.Equal(2, contents.Factions.Count);
            Assert.Contains(contents.Factions, f => f is { FactionId: 500001, Name: "Caldari State" });
            Assert.Contains(contents.Factions, f => f is { FactionId: 500010, Name: "Guristas Pirates" });
        }
        finally
        {
            File.Delete(zipPath);
        }
    }

    [Fact]
    public void ParseZip_MissingFactionsEntry_Throws()
    {
        var zipPath = WriteZip(types: "", solarSystems: "", npcCorporations: "", includeFactionsEntry: false);

        try
        {
            var exception = Assert.Throws<InvalidDataException>(() => SdeDatasetParser.ParseZip(zipPath));

            Assert.Contains("factions.jsonl", exception.Message);
        }
        finally
        {
            File.Delete(zipPath);
        }
    }

    [Fact]
    public void ParseZip_MissingRequiredEntry_Throws()
    {
        var zipPath = Path.Combine(Path.GetTempPath(), $"sde-dataset-{Guid.NewGuid():N}.zip");

        using (var stream = File.Create(zipPath))
        using (new ZipArchive(stream, ZipArchiveMode.Create))
        {
            // No entries at all.
        }

        try
        {
            Assert.Throws<InvalidDataException>(() => SdeDatasetParser.ParseZip(zipPath));
        }
        finally
        {
            File.Delete(zipPath);
        }
    }

    private static string WriteZip(string types, string solarSystems, string npcCorporations, string factions = "", bool includeFactionsEntry = true)
    {
        var zipPath = Path.Combine(Path.GetTempPath(), $"sde-dataset-{Guid.NewGuid():N}.zip");

        using var stream = File.Create(zipPath);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create);

        WriteEntry(archive, "types.jsonl", types);
        WriteEntry(archive, "mapSolarSystems.jsonl", solarSystems);
        WriteEntry(archive, "npcCorporations.jsonl", npcCorporations);

        if (includeFactionsEntry)
            WriteEntry(archive, "factions.jsonl", factions);

        return zipPath;
    }

    private static void WriteEntry(ZipArchive archive, string entryName, string contents)
    {
        var entry = archive.CreateEntry(entryName);
        using var entryStream = entry.Open();
        using var writer = new StreamWriter(entryStream, Encoding.UTF8);
        writer.Write(contents);
    }
}

using Killright.Core.Style;
using Killright.UI.Analysis;
using Killright.UI.Configuration;
using Killright.UI.Controls;
using Killright.UI.Resources;
using Killright.UI.Style;
using Xunit;

namespace Killright.UI.Tests.Resources;

public sealed class BandNamesAndStyleWordsTests
{
    [Fact]
    public void ShippedSettingsFile_HoldsNoBandNames()
    {
        var text = File.ReadAllText(ApplicationSettingsLoader.GetDefaultSettingsPath());
        using var document = System.Text.Json.JsonDocument.Parse(text);

        foreach (var bandsName in new[] { "threatBands", "relationshipConfidenceBands" })
        {
            foreach (var band in document.RootElement.GetProperty(bandsName).EnumerateArray())
                Assert.False(band.TryGetProperty("name", out _));
        }
    }

    [Fact]
    public void Load_BandsWithoutNames_LoadsWithoutLogging()
    {
        var path = Path.Combine(Path.GetTempPath(), $"bands.{Guid.NewGuid():N}.json");
        File.WriteAllText(path, "{ \"threatBands\": [ { \"minimumScore\": 1, \"maximumScore\": 50 }, { \"minimumScore\": 51, \"maximumScore\": 100 } ] }");

        try
        {
            var log = new List<string>();
            var settings = ApplicationSettingsLoader.Load(path, log.Add).Settings!;

            Assert.Equal(2, settings.ThreatBands.Count);
            Assert.DoesNotContain(log, line => line.Contains("threatBands", StringComparison.Ordinal) && line.Contains("built-in", StringComparison.Ordinal) && !line.Contains("missing", StringComparison.Ordinal));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData(1, "1")]
    [InlineData(20, "1")]
    [InlineData(21, "2")]
    [InlineData(61, "4")]
    [InlineData(100, "5")]
    public void ThreatBandMapper_ReturnsBandPosition(int score, string expected)
    {
        Assert.Equal(expected, ThreatBandMapper.MapScore(score, ThreatBandSetting.Defaults));
    }

    [Theory]
    [InlineData("Low")]
    [InlineData("Medium")]
    [InlineData("High")]
    [InlineData("Very High")]
    [InlineData("Extreme")]
    public void ThreatGlyph_EnglishBandName_IsNotMatched(string englishName)
    {
        Assert.Equal("?", ThreatGlyph.Describe(englishName).Text);
    }

    [Theory]
    [InlineData("1", "Low")]
    [InlineData("2", "Medium")]
    [InlineData("3", "High")]
    [InlineData("4", "Very High")]
    [InlineData("5", "Extreme")]
    public void BandNames_ComeFromTheResourceSetByPosition(string position, string expected)
    {
        Assert.Equal(expected, Strings.Get($"ThreatBand.{position}"));
        Assert.Equal(expected, UiText.GetThreatBandDisplay(position));
    }

    [Theory]
    [InlineData(StyleClassification.Victim, "Victim")]
    [InlineData(StyleClassification.SoloBeginner, "Solo (beginner)")]
    [InlineData(StyleClassification.GangBeginner, "Gang (beginner)")]
    public void StyleWords_AreFullWords(StyleClassification style, string expected)
    {
        Assert.Equal(expected, StyleDisplayFormatter.Format(style));
    }

    [Fact]
    public void AngelsAbbreviation_IsAg()
    {
        Assert.Equal("Ag", Strings.Get("Faction.Code.500011"));
    }
}

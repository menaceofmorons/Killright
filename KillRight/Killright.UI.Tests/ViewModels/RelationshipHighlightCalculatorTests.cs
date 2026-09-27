using System.Windows.Media;
using Killright.UI.Analysis;
using Killright.UI.Configuration;
using Killright.UI.ViewModels;
using Xunit;

namespace Killright.UI.Tests.ViewModels;

public sealed class RelationshipHighlightCalculatorTests
{
    private static readonly Color PilotColor = Color.FromRgb(0xAD, 0xD8, 0xE6);
    private static readonly Color RelatedColor = Color.FromRgb(0x90, 0xEE, 0x90);
    private const double Opacity = 0.2;

    private static PilotRelationship Relationship(long a, long b, int strength, int confidence, RelationshipLinkType linkType = RelationshipLinkType.Direct) =>
        new(a, b, linkType, strength, confidence, null, null, null);

    [Fact]
    public void Apply_HoveredRow_GetsPilotRoleAndNoRelationshipText()
    {
        var hovered = new PilotReportRow { CharacterId = 1, Pilot = "Lukas Naarii" };
        var rows = new[] { hovered };

        RelationshipHighlightCalculator.Apply(rows, hovered, _ => false, RelationshipConfidenceBandSetting.Defaults, PilotColor, RelatedColor, Opacity);

        Assert.NotNull(hovered.HighlightBrush);
        Assert.Equal("-", hovered.RelationshipStrengthDisplay);
        Assert.Null(hovered.RelationshipConfidenceBand);
    }

    [Fact]
    public void Apply_SameNonNpcCorporation_GetsSameGroupHighlightNotRelationship()
    {
        var hovered = new PilotReportRow { CharacterId = 1, CorporationId = 2000001, Pilot = "Lukas Naarii" };
        var other = new PilotReportRow
        {
            CharacterId = 2,
            CorporationId = 2000001,
            Pilot = "T'ral Vsengne",
            GroupRelationships = new[] { Relationship(1, 2, 80, 90) }
        };

        RelationshipHighlightCalculator.Apply(
            new[] { hovered, other }, hovered, _ => false, RelationshipConfidenceBandSetting.Defaults, PilotColor, RelatedColor, Opacity);

        Assert.NotNull(other.HighlightBrush);
        Assert.Equal("-", other.RelationshipStrengthDisplay);
        Assert.Null(other.RelationshipConfidenceBand);
    }

    [Fact]
    public void Apply_SameNpcCorporation_NeverCountsAsSameGroup_FallsThroughToRelationship()
    {
        var hovered = new PilotReportRow { CharacterId = 1, CorporationId = 1000001, Pilot = "Lukas Naarii" };
        var other = new PilotReportRow
        {
            CharacterId = 2,
            CorporationId = 1000001,
            Pilot = "T'ral Vsengne",
            GroupRelationships = new[] { Relationship(1, 2, 80, 90) }
        };

        RelationshipHighlightCalculator.Apply(
            new[] { hovered, other }, hovered, _ => true, RelationshipConfidenceBandSetting.Defaults, PilotColor, RelatedColor, Opacity);

        Assert.Equal("80", other.RelationshipStrengthDisplay);
        Assert.Equal("High", other.RelationshipConfidenceBand);
    }

    [Fact]
    public void Apply_DirectRelationship_ShowsStrengthAndMappedConfidenceBand()
    {
        var hovered = new PilotReportRow { CharacterId = 1, Pilot = "Lukas Naarii" };
        var other = new PilotReportRow
        {
            CharacterId = 2,
            Pilot = "T'ral Vsengne",
            GroupRelationships = new[] { Relationship(2, 1, 42, 10) }
        };

        RelationshipHighlightCalculator.Apply(
            new[] { hovered, other }, hovered, _ => false, RelationshipConfidenceBandSetting.Defaults, PilotColor, RelatedColor, Opacity);

        Assert.NotNull(other.HighlightBrush);
        Assert.Equal("42", other.RelationshipStrengthDisplay);
        Assert.Equal("Low", other.RelationshipConfidenceBand);
    }

    [Fact]
    public void Apply_NoRelationshipAndDifferentGroup_NoHighlightAndDash()
    {
        var hovered = new PilotReportRow { CharacterId = 1, Pilot = "Lukas Naarii" };
        var other = new PilotReportRow { CharacterId = 2, Pilot = "syMptom NZ" };

        RelationshipHighlightCalculator.Apply(
            new[] { hovered, other }, hovered, _ => false, RelationshipConfidenceBandSetting.Defaults, PilotColor, RelatedColor, Opacity);

        Assert.Null(other.HighlightBrush);
        Assert.Equal("-", other.RelationshipStrengthDisplay);
        Assert.Null(other.RelationshipConfidenceBand);
    }

    [Fact]
    public void Apply_HoveredRowHasNoCharacterId_ClearsAllHighlights()
    {
        var hovered = new PilotReportRow { CharacterId = null, Pilot = "Unresolved Pilot" };
        var other = new PilotReportRow { CharacterId = 2, Pilot = "syMptom NZ", HighlightBrush = new SolidColorBrush(PilotColor) };

        RelationshipHighlightCalculator.Apply(
            new[] { hovered, other }, hovered, _ => false, RelationshipConfidenceBandSetting.Defaults, PilotColor, RelatedColor, Opacity);

        Assert.Null(hovered.HighlightBrush);
        Assert.Null(other.HighlightBrush);
    }

    [Fact]
    public void Clear_ResetsHighlightAndRelationshipDisplayForAllRows()
    {
        var rows = new[]
        {
            new PilotReportRow { HighlightBrush = new SolidColorBrush(PilotColor), RelationshipStrengthDisplay = "50", RelationshipConfidenceBand = "High" }
        };

        RelationshipHighlightCalculator.Clear(rows);

        Assert.Null(rows[0].HighlightBrush);
        Assert.Equal("-", rows[0].RelationshipStrengthDisplay);
        Assert.Null(rows[0].RelationshipConfidenceBand);
    }
}

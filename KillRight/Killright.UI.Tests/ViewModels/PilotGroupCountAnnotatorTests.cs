using Killright.UI.Analysis;
using Killright.UI.ViewModels;
using Xunit;

namespace Killright.UI.Tests.ViewModels;

public sealed class PilotGroupCountAnnotatorTests
{
    private const long NpcThreshold = 1_005_000;

    private static PilotRelationship Relationship(RelationshipLinkType linkType) =>
        new(1, 2, linkType, 50, 50, null, null, null);

    [Fact]
    public void Annotate_DirectAndChainRelationships_FormatsAsDirectSlashChain()
    {
        var rows = new[]
        {
            new PilotReportRow
            {
                GroupRelationships = new[]
                {
                    Relationship(RelationshipLinkType.Direct),
                    Relationship(RelationshipLinkType.Direct),
                    Relationship(RelationshipLinkType.Direct),
                    Relationship(RelationshipLinkType.Chain)
                }
            }
        };

        PilotGroupCountAnnotator.Annotate(rows, NpcThreshold);

        Assert.Equal("3/1", rows[0].Group);
    }

    [Fact]
    public void Annotate_ZeroRelationshipsOnASide_ShowsDashPerZeroDisplayRule()
    {
        var rows = new[]
        {
            new PilotReportRow { GroupRelationships = new[] { Relationship(RelationshipLinkType.Chain) } }
        };

        PilotGroupCountAnnotator.Annotate(rows, NpcThreshold);

        Assert.Equal("-/1", rows[0].Group);
    }

    [Fact]
    public void Annotate_NoGroupRelationships_ShowsDashDash()
    {
        var rows = new[] { new PilotReportRow() };

        PilotGroupCountAnnotator.Annotate(rows, NpcThreshold);

        Assert.Equal("-/-", rows[0].Group);
    }

    [Fact]
    public void Annotate_TwoPilotsSameCorporation_AppendsCountToBoth()
    {
        var rows = new[]
        {
            new PilotReportRow { CorporationId = 2000001, Corporation = "Acme" },
            new PilotReportRow { CorporationId = 2000001, Corporation = "Acme" }
        };

        PilotGroupCountAnnotator.Annotate(rows, NpcThreshold);

        Assert.All(rows, row => Assert.Equal("Acme [2]", row.Corporation));
    }

    [Fact]
    public void Annotate_SinglePilotInCorporation_NoCountAppended()
    {
        var rows = new[] { new PilotReportRow { CorporationId = 2000001, Corporation = "Acme" } };

        PilotGroupCountAnnotator.Annotate(rows, NpcThreshold);

        Assert.Equal("Acme", rows[0].Corporation);
    }

    [Fact]
    public void Annotate_NullCorporationId_NeverCounted()
    {
        var rows = new[]
        {
            new PilotReportRow { CorporationId = null, Corporation = "unk" },
            new PilotReportRow { CorporationId = null, Corporation = "unk" }
        };

        PilotGroupCountAnnotator.Annotate(rows, NpcThreshold);

        Assert.All(rows, row => Assert.Equal("unk", row.Corporation));
    }

    [Fact]
    public void Annotate_NpcCorporationId_NeverCounted()
    {
        var rows = new[]
        {
            new PilotReportRow { CorporationId = 1000001, Corporation = "NPC Corp" },
            new PilotReportRow { CorporationId = 1000001, Corporation = "NPC Corp" }
        };

        PilotGroupCountAnnotator.Annotate(rows, NpcThreshold);

        Assert.All(rows, row => Assert.Equal("NPC Corp", row.Corporation));
    }

    [Fact]
    public void Annotate_NullAllianceId_NeverCounted()
    {
        var rows = new[]
        {
            new PilotReportRow { AllianceId = null, Alliance = "None" },
            new PilotReportRow { AllianceId = null, Alliance = "None" }
        };

        PilotGroupCountAnnotator.Annotate(rows, NpcThreshold);

        Assert.All(rows, row => Assert.Equal("None", row.Alliance));
    }

    [Fact]
    public void Annotate_ThreePilotsSameAlliance_AppendsCountOfThree()
    {
        var rows = new[]
        {
            new PilotReportRow { AllianceId = 99000001, Alliance = "Federation" },
            new PilotReportRow { AllianceId = 99000001, Alliance = "Federation" },
            new PilotReportRow { AllianceId = 99000001, Alliance = "Federation" }
        };

        PilotGroupCountAnnotator.Annotate(rows, NpcThreshold);

        Assert.All(rows, row => Assert.Equal("Federation [3]", row.Alliance));
    }
}

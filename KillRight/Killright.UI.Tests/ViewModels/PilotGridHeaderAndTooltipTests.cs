using System.IO;
using System.Xml.Linq;
using Killright.Core.Style;
using Killright.UI.Style;
using Killright.UI.ViewModels;
using Xunit;

namespace Killright.UI.Tests.ViewModels;

public sealed class PilotGridHeaderAndTooltipTests
{
    private static readonly XNamespace Presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    private static XDocument LoadMainWindowXaml()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "KillRight.sln")))
            directory = directory.Parent;

        return XDocument.Load(Path.Combine(directory!.FullName, "Killright.UI", "MainWindow.xaml"));
    }

    private static XElement Column(string name) =>
        LoadMainWindowXaml().Descendants().Single(element => (string?)element.Attribute(Xaml + "Name") == name);

    [Theory]
    [InlineData("ColumnGroup")]
    [InlineData("ColumnRelationship")]
    public void Column_WithoutSortPath_CannotBeSorted(string columnName)
    {
        var column = Column(columnName);

        Assert.Equal("False", (string?)column.Attribute("CanUserSort"));
        Assert.Null(column.Attribute("SortMemberPath"));
    }

    [Theory]
    [InlineData("ColumnThreat", "ThreatSortKey")]
    [InlineData("ColumnSecurityStatus", "SecuritySortKey")]
    [InlineData("ColumnWeek", "WeekSortKey")]
    [InlineData("ColumnLastActive", "LastKillSortKey")]
    public void SortableColumns_KeepTheirSortKeys(string columnName, string sortKey)
    {
        var column = Column(columnName);

        Assert.Equal(sortKey, (string?)column.Attribute("SortMemberPath"));
        Assert.Null(column.Attribute("CanUserSort"));
    }

    [Fact]
    public void ColumnHeaderStyle_IsBold()
    {
        var style = LoadMainWindowXaml().Descendants(Presentation + "Style")
            .Single(element => (string?)element.Attribute(Xaml + "Key") == "ColumnHeaderStyle");

        var weight = style.Elements(Presentation + "Setter").Single(setter => (string?)setter.Attribute("Property") == "FontWeight");

        Assert.Equal("Bold", (string?)weight.Attribute("Value"));
    }

    [Theory]
    [InlineData(StyleClassification.Solo, StyleClassification.Gang)]
    [InlineData(StyleClassification.Blob, StyleClassification.Inactive)]
    [InlineData(StyleClassification.Unknown, StyleClassification.Victim)]
    public void StyleTooltip_ShowsGeneralAndRecentWords(StyleClassification general, StyleClassification recent)
    {
        var row = new PilotReportRow
        {
            GeneralStyle = StyleDisplayFormatter.Format(general),
            RecentStyle = StyleDisplayFormatter.Format(recent)
        };

        Assert.Equal($"{StyleDisplayFormatter.Format(general)} / {StyleDisplayFormatter.Format(recent)}", row.StyleTooltip);
    }

    [Fact]
    public void StyleTooltip_BlankCell_IsNull()
    {
        Assert.Null(new PilotReportRow().StyleTooltip);
    }

    [Theory]
    [InlineData(500001L, "Caldari State")]
    [InlineData(500004L, "Gallente Federation")]
    [InlineData(500002L, "Minmatar Republic")]
    [InlineData(500003L, "Amarr Empire")]
    [InlineData(500011L, "Angel Cartel")]
    [InlineData(500010L, "Guristas Pirates")]
    public void FactionTooltip_GivesFullName(long factionId, string name)
    {
        Assert.Equal(name, PilotGridTooltips.FactionName(factionId, _ => name));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(123L)]
    public void FactionTooltip_BlankCell_IsNull(long? factionId)
    {
        Assert.Null(PilotGridTooltips.FactionName(factionId, _ => "Name"));
    }

    [Fact]
    public void FactionTooltip_NameNotFound_IsNull()
    {
        Assert.Null(PilotGridTooltips.FactionName(500001, _ => null));
    }
}

using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows.Data;
using Killright.UI.ViewModels;
using Xunit;

namespace Killright.UI.Tests.ViewModels;

public sealed class ReplaceableObservableCollectionTests
{
    private static PilotReportRow Row(long characterId, string name) =>
        new() { CharacterId = characterId, InputName = name, Pilot = name };

    [Fact]
    public void ReplaceAll_RaisesExactlyOneResetAndKeepsListOrder()
    {
        var collection = new ReplaceableObservableCollection<PilotReportRow>();
        collection.Add(Row(9, "Kljucaonica"));

        var events = new List<NotifyCollectionChangedEventArgs>();
        collection.CollectionChanged += (_, args) => events.Add(args);

        var rows = new[] { Row(1, "Lukas Naarii"), Row(2, "T'ral Vsengne"), Row(3, "syMptom NZ") };

        collection.ReplaceAll(rows);

        var single = Assert.Single(events);
        Assert.Equal(NotifyCollectionChangedAction.Reset, single.Action);
        Assert.Equal(rows, collection);
    }

    [Fact]
    public void ReplaceAll_NotifiesCountAndIndexer()
    {
        var collection = new ReplaceableObservableCollection<PilotReportRow>();
        var properties = new List<string?>();
        ((INotifyPropertyChanged)collection).PropertyChanged += (_, args) => properties.Add(args.PropertyName);

        collection.ReplaceAll(new[] { Row(1, "Lukas Naarii") });

        Assert.Contains("Count", properties);
        Assert.Contains("Item[]", properties);
    }

    [Fact]
    public void ReplaceAll_EmptyList_ClearsWithOneReset()
    {
        var collection = new ReplaceableObservableCollection<PilotReportRow> { Row(1, "Lukas Naarii") };
        var resets = 0;
        collection.CollectionChanged += (_, args) => resets += args.Action == NotifyCollectionChangedAction.Reset ? 1 : 0;

        collection.ReplaceAll(Array.Empty<PilotReportRow>());

        Assert.Empty(collection);
        Assert.Equal(1, resets);
    }

    [Fact]
    public void ReplaceAll_ThroughACollectionView_RefreshesOnceAndAppliesFilterAndSort()
    {
        var collection = new ReplaceableObservableCollection<PilotReportRow>();
        var view = new ListCollectionView(collection)
        {
            Filter = item => item is PilotReportRow row && row.Pilot != "syMptom NZ"
        };
        view.SortDescriptions.Add(new SortDescription(nameof(PilotReportRow.Pilot), ListSortDirection.Ascending));

        var viewEvents = 0;
        ((INotifyCollectionChanged)view).CollectionChanged += (_, _) => viewEvents++;

        collection.ReplaceAll(new[] { Row(3, "syMptom NZ"), Row(2, "T'ral Vsengne"), Row(1, "Lukas Naarii") });

        Assert.Equal(1, viewEvents);
        Assert.Equal(["Lukas Naarii", "T'ral Vsengne"], view.Cast<PilotReportRow>().Select(row => row.Pilot));
    }

    [Fact]
    public void ReplaceAll_ReplacementRowsKeepTheirOwnFlagsAndHighlightState()
    {
        var collection = new ReplaceableObservableCollection<PilotReportRow>();
        var kept = Row(1, "Lukas Naarii");
        kept.IsNewPilot = true;

        collection.ReplaceAll(new[] { kept, Row(2, "T'ral Vsengne") });

        Assert.True(collection[0].IsNewPilot);
        Assert.Equal(RowBackgroundKind.NewPilot, collection[0].RowBackgroundKind);
        Assert.False(collection[1].IsNewPilot);
    }
}

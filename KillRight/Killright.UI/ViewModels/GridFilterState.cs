using Killright.Core.Style;
using Killright.UI.UiState;

namespace Killright.UI.ViewModels;

public enum GridFilterField
{
    Corporation,
    Alliance,
    FactionWarfare,
    GeneralStyle,
    RecentStyle
}

public enum GridFilterMode
{
    Ignore,
    FilterTo
}

public readonly record struct GridFilterEntry(GridFilterField Field, GridFilterMode Mode, long Key);

public sealed class GridFilterState
{
    public const string IgnoreHeaderPrefix = "(I) ";
    public const string FilterHeaderPrefix = "(F) ";

    private readonly List<GridFilterEntry> _entries = new();
    private HashSet<PilotReportRow>? _pilotVisibleRows;

    public IReadOnlyList<GridFilterEntry> Entries => _entries;

    public bool IsPilotFilterActive => _pilotVisibleRows is not null;

    public bool IsActive => _entries.Count > 0 || _pilotVisibleRows is not null;

    public static bool TryGetKey(PilotReportRow row, GridFilterField field, out long key)
    {
        switch (field)
        {
            case GridFilterField.Corporation:
                key = row.CorporationId.GetValueOrDefault();
                return row.CorporationId.HasValue;
            case GridFilterField.Alliance:
                key = row.AllianceId.GetValueOrDefault();
                return row.AllianceId.HasValue;
            case GridFilterField.FactionWarfare:
                key = row.FactionId.GetValueOrDefault();
                return row.FactionId.HasValue;
            case GridFilterField.GeneralStyle:
                return TryGetStyleKey(row.GeneralStyleClassification, out key);
            default:
                return TryGetStyleKey(row.RecentStyleClassification, out key);
        }
    }

    public static string ColumnIdOf(GridFilterField field) => field switch
    {
        GridFilterField.Corporation => ColumnIds.Corporation,
        GridFilterField.Alliance => ColumnIds.Alliance,
        GridFilterField.FactionWarfare => ColumnIds.FactionWarfare,
        _ => ColumnIds.Style
    };

    public bool Add(PilotReportRow row, GridFilterField field, GridFilterMode mode)
    {
        if (!TryGetKey(row, field, out var key))
            return false;

        var entry = new GridFilterEntry(field, mode, key);

        if (_entries.Contains(entry))
            return false;

        _entries.Add(entry);
        return true;
    }

    public bool ApplyPilotFilter(PilotReportRow pilot, IEnumerable<PilotReportRow> rows, Func<long, bool> isNpcCorporation)
    {
        if (_pilotVisibleRows is not null || pilot.CharacterId is null)
            return false;

        var visible = new HashSet<PilotReportRow>();

        foreach (var row in rows)
        {
            if (ReferenceEquals(row, pilot)
                || RelationshipHighlightCalculator.IsSameGroup(row, pilot, isNpcCorporation)
                || RelationshipHighlightCalculator.FindRelationship(row, pilot) is not null)
            {
                visible.Add(row);
            }
        }

        _pilotVisibleRows = visible;
        return true;
    }

    public bool IsVisible(PilotReportRow row)
    {
        if (_pilotVisibleRows is not null && !_pilotVisibleRows.Contains(row))
            return false;

        foreach (var entry in _entries)
        {
            var matches = TryGetKey(row, entry.Field, out var key) && key == entry.Key;

            if (entry.Mode == GridFilterMode.Ignore ? matches : !matches)
                return false;
        }

        return true;
    }

    public string HeaderPrefix(string columnId)
    {
        if (columnId == ColumnIds.Pilot)
            return _pilotVisibleRows is not null ? FilterHeaderPrefix : string.Empty;

        var onColumn = _entries.Where(entry => ColumnIdOf(entry.Field) == columnId).ToList();

        if (onColumn.Count == 0)
            return string.Empty;

        return onColumn.Any(entry => entry.Mode == GridFilterMode.FilterTo) ? FilterHeaderPrefix : IgnoreHeaderPrefix;
    }

    public bool CarriesFilter(string columnId) => HeaderPrefix(columnId).Length > 0;

    public void Clear()
    {
        _entries.Clear();
        _pilotVisibleRows = null;
    }

    private static bool TryGetStyleKey(StyleClassification classification, out long key)
    {
        var baseClassification = classification switch
        {
            StyleClassification.SoloBeginner => StyleClassification.Solo,
            StyleClassification.GangBeginner => StyleClassification.Gang,
            _ => classification
        };

        key = (long)baseClassification;
        return baseClassification != StyleClassification.Unknown;
    }
}

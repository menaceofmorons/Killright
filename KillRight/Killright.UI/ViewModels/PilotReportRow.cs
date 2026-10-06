using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using Killright.UI.Analysis;

namespace Killright.UI.ViewModels;

public enum RowBackgroundKind
{
    None,
    NewPilot,
    Highlight
}

public class PilotReportRow : INotifyPropertyChanged
{
    public long? CharacterId { get; set; }
    public string InputName { get; set; } = string.Empty;
    public IReadOnlyList<PilotRelationship> GroupRelationships { get; set; } = Array.Empty<PilotRelationship>();
    public string Pilot { get; set; } = string.Empty;
    public bool EngineAnalysisFailed { get; set; }
    public string Verify { get; set; } = string.Empty;
    public string Threat { get; set; } = string.Empty;
    public string SecurityStatus { get; set; } = string.Empty;
    public string Group { get; set; } = string.Empty;
    public long? CorporationId { get; set; }
    public string Corporation { get; set; } = string.Empty;
    public string CorporationPlain { get; set; } = string.Empty;
    public long? AllianceId { get; set; }
    public string Alliance { get; set; } = string.Empty;
    public string AlliancePlain { get; set; } = string.Empty;
    public long? FactionId { get; set; }
    public string FactionWarfare { get; set; } = string.Empty;
    public string Style { get; set; } = string.Empty;
    public string GeneralStyle { get; set; } = string.Empty;
    public string RecentStyle { get; set; } = string.Empty;
    public string Week { get; set; } = string.Empty;
    public string Kills { get; set; } = string.Empty;
    public string Solos { get; set; } = string.Empty;
    public string LastKill { get; set; } = string.Empty;
    public string InfoWeekKills { get; set; } = string.Empty;
    public string InfoWeekSolos { get; set; } = string.Empty;
    public string InfoWeekLosses { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
    public string? StatsFailureSource { get; set; }

    private Brush? _highlightBrush;
    public Brush? HighlightBrush
    {
        get => _highlightBrush;
        set
        {
            SetField(ref _highlightBrush, value, nameof(HasHighlight));
            OnPropertyChanged(nameof(RowBackgroundKind));
        }
    }

    public bool HasHighlight => _highlightBrush is not null;

    private bool _isNewPilot;
    public bool IsNewPilot
    {
        get => _isNewPilot;
        set => SetField(ref _isNewPilot, value, nameof(RowBackgroundKind));
    }

    public RowBackgroundKind RowBackgroundKind => _highlightBrush is not null
        ? RowBackgroundKind.Highlight
        : _isNewPilot
            ? RowBackgroundKind.NewPilot
            : RowBackgroundKind.None;

    private string _relationshipStrengthDisplay = "-";
    public string RelationshipStrengthDisplay
    {
        get => _relationshipStrengthDisplay;
        set => SetField(ref _relationshipStrengthDisplay, value);
    }

    private string? _relationshipConfidenceBand;
    public string? RelationshipConfidenceBand
    {
        get => _relationshipConfidenceBand;
        set => SetField(ref _relationshipConfidenceBand, value);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged(string propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private void SetField<T>(ref T field, T value, string? alsoNotify = null, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return;

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        if (alsoNotify is not null)
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(alsoNotify));
    }
}
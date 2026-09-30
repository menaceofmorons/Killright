using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using Killright.UI.Resources;
using Killright.UI.ViewModels;

namespace Killright.UI.InfoSheet;

public sealed class InfoSheetViewModel : INotifyPropertyChanged
{
    private readonly PilotReportRow _row;
    private readonly Func<string, Task<DateOnly?>> _loadBirthday;
    private readonly Func<long, Task<PilotLastActivitySummary?>> _loadLastActivity;

    private string _errorLine;
    private Visibility _errorLineVisibility;
    private string _birthdayValue;
    private string _lastActivityDateTimeValue;
    private string _lastActivityKillLossValue;
    private string _lastActivitySystemValue;
    private string _lastActivityShipValue;
    private string _lastActivityWeaponValue;
    private string _lastActivityVictimValue;
    private string _lastActivityAttackersValue;
    private Visibility _lastActivityKillOnlyVisibility;

    public string StatsTitle { get; }
    public string ThreatValue { get; }
    public string SecValue { get; }
    public string KillsValue { get; }
    public string SolosValue { get; }
    public string GeneralValue { get; }
    public string RecentValue { get; }
    public Visibility DeveloperFieldsVisibility { get; }
    public string VerifyValue { get; }
    public string NotesValue { get; }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string ErrorLine
    {
        get => _errorLine;
        private set => SetField(ref _errorLine, value);
    }

    public Visibility ErrorLineVisibility
    {
        get => _errorLineVisibility;
        private set => SetField(ref _errorLineVisibility, value);
    }

    public string BirthdayValue
    {
        get => _birthdayValue;
        private set => SetField(ref _birthdayValue, value);
    }

    public string LastActivityDateTimeValue
    {
        get => _lastActivityDateTimeValue;
        private set => SetField(ref _lastActivityDateTimeValue, value);
    }

    public string LastActivityKillLossValue
    {
        get => _lastActivityKillLossValue;
        private set => SetField(ref _lastActivityKillLossValue, value);
    }

    public string LastActivitySystemValue
    {
        get => _lastActivitySystemValue;
        private set => SetField(ref _lastActivitySystemValue, value);
    }

    public string LastActivityShipValue
    {
        get => _lastActivityShipValue;
        private set => SetField(ref _lastActivityShipValue, value);
    }

    public string LastActivityWeaponValue
    {
        get => _lastActivityWeaponValue;
        private set => SetField(ref _lastActivityWeaponValue, value);
    }

    public string LastActivityVictimValue
    {
        get => _lastActivityVictimValue;
        private set => SetField(ref _lastActivityVictimValue, value);
    }

    public string LastActivityAttackersValue
    {
        get => _lastActivityAttackersValue;
        private set => SetField(ref _lastActivityAttackersValue, value);
    }

    public Visibility LastActivityKillOnlyVisibility
    {
        get => _lastActivityKillOnlyVisibility;
        private set => SetField(ref _lastActivityKillOnlyVisibility, value);
    }

    public InfoSheetViewModel(
        PilotReportRow row,
        bool developerModeRevealed,
        Func<string, Task<DateOnly?>> loadBirthday,
        Func<long, Task<PilotLastActivitySummary?>> loadLastActivity)
    {
        _row = row;
        _loadBirthday = loadBirthday;
        _loadLastActivity = loadLastActivity;

        StatsTitle = UiText.FormatStatsTitle(row.Pilot);
        _errorLine = row.StatsFailureSource is null ? string.Empty : UiText.FormatErrorLoading(row.StatsFailureSource);
        _errorLineVisibility = row.StatsFailureSource is null ? Visibility.Collapsed : Visibility.Visible;
        ThreatValue = row.Threat;
        SecValue = row.SecurityStatus;
        KillsValue = row.Kills;
        SolosValue = row.Solos;
        GeneralValue = row.GeneralStyle;
        RecentValue = row.RecentStyle;
        DeveloperFieldsVisibility = developerModeRevealed ? Visibility.Visible : Visibility.Collapsed;
        VerifyValue = row.Verify;
        NotesValue = row.Notes;

        var loading = UiText.InfoSheetLoading;
        _birthdayValue = loading;
        _lastActivityDateTimeValue = loading;
        _lastActivityKillLossValue = loading;
        _lastActivitySystemValue = loading;
        _lastActivityShipValue = loading;
        _lastActivityWeaponValue = loading;
        _lastActivityVictimValue = loading;
        _lastActivityAttackersValue = loading;
        _lastActivityKillOnlyVisibility = Visibility.Collapsed;
    }

    public async Task LoadAsync()
    {
        var failedSources = new List<string>();

        try
        {
            var birthday = await _loadBirthday(_row.InputName);
            BirthdayValue = birthday?.ToString("yyyy-MM-dd") ?? "unk";
        }
        catch
        {
            BirthdayValue = "unk";
            failedSources.Add(UiText.InfoSheetSourceBirthday);
        }

        try
        {
            var lastActivity = _row.CharacterId is { } characterId
                ? await _loadLastActivity(characterId)
                : null;

            ApplyLastActivity(lastActivity);
        }
        catch
        {
            ApplyLastActivity(null);
            failedSources.Add(UiText.InfoSheetSourceLastActivity);
        }

        if (failedSources.Count == 0)
            return;

        var sources = new List<string>();

        if (_row.StatsFailureSource is not null)
            sources.Add(_row.StatsFailureSource);

        sources.AddRange(failedSources);

        ErrorLine = UiText.FormatErrorLoading(string.Join(", ", sources));
        ErrorLineVisibility = Visibility.Visible;
    }

    private void ApplyLastActivity(PilotLastActivitySummary? lastActivity)
    {
        LastActivityDateTimeValue = lastActivity?.DateTime ?? "-";
        LastActivityKillLossValue = lastActivity?.KillLoss ?? "-";
        LastActivitySystemValue = lastActivity?.System ?? "-";
        LastActivityShipValue = lastActivity?.Ship ?? "-";
        LastActivityWeaponValue = lastActivity?.Weapon ?? "-";
        LastActivityVictimValue = lastActivity?.Victim ?? "-";
        LastActivityAttackersValue = lastActivity?.Attackers ?? "-";
        LastActivityKillOnlyVisibility = lastActivity is { IsKill: true } ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return;

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

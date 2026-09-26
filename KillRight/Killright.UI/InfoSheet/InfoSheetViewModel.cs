using System.Windows;
using Killright.UI.ViewModels;

namespace Killright.UI.InfoSheet;

public sealed class InfoSheetViewModel
{
    public string StatsTitle { get; }
    public string ErrorLine { get; }
    public Visibility ErrorLineVisibility { get; }
    public string BirthdayValue { get; }
    public string ThreatValue { get; }
    public string SecValue { get; }
    public string KillsValue { get; }
    public string SolosValue { get; }
    public string GeneralValue { get; }
    public string RecentValue { get; }
    public Visibility DeveloperFieldsVisibility { get; }
    public string VerifyValue { get; }
    public string NotesValue { get; }
    public string LastActivityDateTimeValue { get; }
    public string LastActivityKillLossValue { get; }
    public string LastActivitySystemValue { get; }
    public string LastActivityShipValue { get; }
    public string LastActivityWeaponValue { get; }
    public string LastActivityVictimValue { get; }
    public string LastActivityAttackersValue { get; }
    public Visibility LastActivityKillOnlyVisibility { get; }

    public InfoSheetViewModel(PilotReportRow row, bool developerModeRevealed)
    {
        StatsTitle = $"Stats for {row.Pilot}";
        ErrorLine = row.StatsFailureSource is null ? string.Empty : $"Error loading {row.StatsFailureSource}";
        ErrorLineVisibility = row.StatsFailureSource is null ? Visibility.Collapsed : Visibility.Visible;
        BirthdayValue = row.Birthday;
        ThreatValue = row.Threat;
        SecValue = row.SecurityStatus;
        KillsValue = row.Kills;
        SolosValue = row.Solos;
        GeneralValue = row.GeneralStyle;
        RecentValue = row.RecentStyle;
        DeveloperFieldsVisibility = developerModeRevealed ? Visibility.Visible : Visibility.Collapsed;
        VerifyValue = row.Verify;
        NotesValue = row.Notes;

        var lastActivity = row.LastActivity;
        LastActivityDateTimeValue = lastActivity?.DateTime ?? "-";
        LastActivityKillLossValue = lastActivity?.KillLoss ?? "-";
        LastActivitySystemValue = lastActivity?.System ?? "-";
        LastActivityShipValue = lastActivity?.Ship ?? "-";
        LastActivityWeaponValue = lastActivity?.Weapon ?? "-";
        LastActivityVictimValue = lastActivity?.Victim ?? "-";
        LastActivityAttackersValue = lastActivity?.Attackers ?? "-";
        LastActivityKillOnlyVisibility = lastActivity is { IsKill: true } ? Visibility.Visible : Visibility.Collapsed;
    }
}

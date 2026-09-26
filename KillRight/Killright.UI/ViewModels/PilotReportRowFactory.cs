using Killright.Core.Models;
using Killright.Core.Style;
using Killright.Integration.zKill;
using Killright.Shared;
using Killright.Shared.Time;
using Killright.Shared.zKill;
using Killright.Storage.Killmails;

namespace Killright.UI.ViewModels;

public static class PilotReportRowFactory
{
    public static PilotReportRow FromPilot(
        Pilot pilot,
        zKillActivity? activity,
        zKillStatistics? statistics,
        StyleClassification recentStyle,
        string threatBand,
        bool statisticsCallFailed,
        bool recentCallFailed,
        string? engineFailureReason,
        DateOnly? birthday,
        PilotRecentKillmail? lastActivity)
    {
        var generalStyle = GeneralStyleClassifier.Classify(statistics);

        return new PilotReportRow
        {
            CharacterId = pilot.CharacterId,
            Pilot = GetPilotName(pilot),
            EngineAnalysisFailed = engineFailureReason is not null,
            Verify = GetVerifyDisplay(pilot.VerifyStatus),
            Threat = string.IsNullOrWhiteSpace(threatBand)
                ? "Unk"
                : threatBand,
            SecurityStatus = pilot.SecurityStatus?.ToString("0.00") ?? "unk",
            Group = "unk",
            CorporationId = pilot.Corporation?.CorporationId,
            Corporation = pilot.Corporation?.Name ?? "unk",
            AllianceId = pilot.Alliance?.AllianceId,
            Alliance = GetAllianceDisplay(pilot),
            Style = $"{StyleLetterCodeFormatter.FormatGeneral(generalStyle)}/{StyleLetterCodeFormatter.FormatRecent(recentStyle)}",
            GeneralStyle = StyleDisplayFormatter.Format(generalStyle),
            RecentStyle = StyleDisplayFormatter.Format(recentStyle),
            Week = $"{FormatActivityValue(activity?.HasPublicActivityData, activity?.KillsWeek)}/{FormatActivityValue(activity?.HasPublicActivityData, activity?.SoloWeek)}",
            Kills = FormatActivityValue(activity?.HasPublicActivityData, activity?.KillsWeek),
            Solos = FormatActivityValue(activity?.HasPublicActivityData, activity?.SoloWeek),
            LastKill = FormatLastKill(activity),
            Notes = GetNotes(activity, statistics, statisticsCallFailed, recentCallFailed, engineFailureReason),
            Birthday = birthday?.ToString("yyyy-MM-dd") ?? "unk",
            StatsFailureSource = GetStatsFailureSource(statisticsCallFailed, recentCallFailed, activity, engineFailureReason),
            LastActivity = BuildLastActivitySummary(lastActivity)
        };
    }

    private static string GetPilotName(Pilot pilot)
    {
        return string.IsNullOrWhiteSpace(pilot.CharacterName) ? pilot.InputName : pilot.CharacterName;
    }

    private static string GetVerifyDisplay(VerifyStatus verifyStatus)
    {
        return verifyStatus switch
        {
            VerifyStatus.Verified => "V",
            VerifyStatus.Partial => "P",
            _ => "unk"
        };
    }

    private static string GetAllianceDisplay(Pilot pilot)
    {
        if (pilot.Alliance is not null)
            return pilot.Alliance.Name;

        return pilot.AllianceId is null ? "None" : "unk";
    }

    private static string FormatActivityValue(bool? hasPublicActivityData, int? value)
    {
        if (hasPublicActivityData != true)
            return "-";

        return value?.ToString() ?? "-";
    }

    private static string FormatLastKill(zKillActivity? activity)
    {
        if (activity?.HasPublicActivityData != true)
            return "-";

        if (activity.LastActivityType != zKillActivityType.Kill || activity.LastActiveUtc is null)
            return "-";

        return FormatKillAge(ApplicationClock.UtcNow - activity.LastActiveUtc.Value);
    }

    internal static string FormatKillAge(TimeSpan age)
    {
        if (age.TotalHours < 24)
            return $"{Math.Max(1, (int)Math.Ceiling(age.TotalHours))}h";

        if (age.TotalDays < 30)
            return $"{(int)age.TotalDays}d";

        return ">30d";
    }

    private static string GetNotes(
        zKillActivity? activity,
        zKillStatistics? statistics,
        bool statisticsCallFailed,
        bool recentCallFailed,
        string? engineFailureReason)
    {
        if (engineFailureReason is not null)
            return $"ESI identity loaded; killright_engine analysis failed ({engineFailureReason}).";

        if (statisticsCallFailed)
            return "ESI identity loaded; zKill statistics call failed.";

        if (activity is null)
            return "ESI identity loaded; zKill activity not loaded.";

        if (!string.IsNullOrWhiteSpace(activity.Error))
            return "ESI identity loaded; zKill unavailable.";

        if (!activity.HasPublicActivityData)
        {
            if (recentCallFailed)
                return "ESI identity loaded; zKill recent killmail call failed.";

            return statistics is null
                ? "ESI identity loaded; no public zKill activity or stats available."
                : "ESI identity loaded; zKill stats loaded; no recent activity.";
        }

        return $"Public zKill activity checked {activity.CheckedAtUtc:yyyy-MM-dd HH:mm} UTC.";
    }

    private static string? GetStatsFailureSource(
        bool statisticsCallFailed,
        bool recentCallFailed,
        zKillActivity? activity,
        string? engineFailureReason)
    {
        if (engineFailureReason is not null)
            return "killright_engine";

        if (statisticsCallFailed)
            return "zKill statistics";

        if (recentCallFailed || activity is null)
            return "zKill recent killmails";

        return null;
    }

    private static PilotLastActivitySummary? BuildLastActivitySummary(PilotRecentKillmail? lastActivity)
    {
        if (lastActivity is null)
            return null;

        var isKill = lastActivity.ActivityType == zKillActivityType.Kill;

        return new PilotLastActivitySummary
        {
            DateTime = lastActivity.KillTimeUtc.UtcDateTime.ToString("yyyy-MM-dd HH:mm"),
            KillLoss = isKill ? "Kill" : "Loss",
            System = "—",
            Ship = lastActivity.ShipTypeId?.ToString() ?? "—",
            Weapon = "—",
            Victim = isKill ? lastActivity.VictimShipTypeId?.ToString() ?? "—" : "—",
            Attackers = isKill ? lastActivity.AttackerCount?.ToString() ?? "—" : "—",
            IsKill = isKill
        };
    }
}
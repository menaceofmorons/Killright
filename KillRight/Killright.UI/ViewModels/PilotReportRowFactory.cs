using Killright.Core.Models;
using Killright.Core.Style;
using Killright.Integration.zKill;
using Killright.Shared;
using Killright.Shared.Time;
using Killright.Shared.zKill;
using Killright.UI.Analysis;
using Killright.UI.Resources;
using Killright.UI.Style;

namespace Killright.UI.ViewModels;

public static class PilotReportRowFactory
{
    public static PilotReportRow FromPilot(
        Pilot pilot,
        zKillActivity? activity,
        zKillStatistics? statistics,
        StyleClassification recentStyle,
        bool recentIsPodder,
        string threatBand,
        bool statisticsCallFailed,
        bool recentCallFailed,
        string? engineFailureReason,
        PilotDerivedActivity? derivedActivity = null)
    {
        var generalResult = GeneralStyleClassifier.Classify(statistics);

        return new PilotReportRow
        {
            CharacterId = pilot.CharacterId,
            InputName = pilot.InputName,
            Pilot = GetPilotName(pilot),
            EngineAnalysisFailed = engineFailureReason is not null,
            Verify = GetVerifyDisplay(pilot.VerifyStatus),
            Threat = string.IsNullOrWhiteSpace(threatBand)
                ? UiText.ThreatBandUnk
                : threatBand,
            SecurityStatus = pilot.SecurityStatus?.ToString("0.00") ?? "unk",
            Group = "unk",
            CorporationId = pilot.Corporation?.CorporationId,
            Corporation = pilot.Corporation?.Name ?? "unk",
            CorporationPlain = pilot.Corporation?.Name ?? "unk",
            AllianceId = pilot.Alliance?.AllianceId,
            Alliance = GetAllianceDisplay(pilot),
            AlliancePlain = GetAllianceDisplay(pilot),
            FactionId = pilot.FactionId,
            FactionWarfare = FormatFactionWarfare(pilot.FactionId, 1),
            Style = $"{StyleLetterCodeFormatter.FormatGeneral(generalResult.Classification, generalResult.IsPodder)}/{StyleLetterCodeFormatter.FormatRecent(recentStyle, recentIsPodder)}",
            GeneralStyle = StyleDisplayFormatter.Format(generalResult.Classification, generalResult.IsPodder),
            RecentStyle = StyleDisplayFormatter.Format(recentStyle, recentIsPodder),
            Week = $"{FormatWeekSideValue(activity?.HasPublicActivityData, activity?.KillsWeek)}/{FormatWeekSideValue(activity?.HasPublicActivityData, activity?.SoloWeek)}",
            Kills = FormatActivityValue(activity?.HasPublicActivityData, activity?.KillsWeek),
            Solos = FormatActivityValue(activity?.HasPublicActivityData, activity?.SoloWeek),
            LastKill = FormatLastKill(activity),
            InfoWeekKills = FormatWeekSideValue(activity?.HasPublicActivityData, activity?.KillsWeek),
            InfoWeekSolos = FormatWeekSideValue(activity?.HasPublicActivityData, activity?.SoloWeek),
            InfoWeekLosses = FormatInfoCount(derivedActivity?.InfoWeekLosses),
            Notes = GetNotes(activity, statistics, statisticsCallFailed, recentCallFailed, engineFailureReason),
            StatsFailureSource = GetStatsFailureSource(statisticsCallFailed, recentCallFailed, activity, engineFailureReason)
        };
    }

    internal static string FormatFactionWarfare(long? factionId, int sameFactionCount)
    {
        var code = UiText.FactionShortCode(factionId);

        if (code is null)
            return UiText.PlaceholderDash;

        return sameFactionCount >= 2 ? $"{code} ({sameFactionCount})" : code;
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

        return string.Empty;
    }

    private static string FormatActivityValue(bool? hasPublicActivityData, int? value)
    {
        if (hasPublicActivityData != true)
            return UiText.PlaceholderDash;

        return value?.ToString() ?? UiText.PlaceholderDash;
    }

    internal static string FormatInfoCount(int? value)
    {
        return value is > 0 ? value.Value.ToString() : UiText.PlaceholderDash;
    }

    private static string FormatWeekSideValue(bool? hasPublicActivityData, int? value)
    {
        var formatted = FormatActivityValue(hasPublicActivityData, value);
        return formatted == "0" ? UiText.PlaceholderDash : formatted;
    }

    private static string FormatLastKill(zKillActivity? activity)
    {
        if (activity?.LastKillUtc is not { } lastKillUtc)
            return UiText.PlaceholderDash;

        return FormatKillAge(ApplicationClock.UtcNow - lastKillUtc);
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
}

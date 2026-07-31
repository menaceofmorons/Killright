using PilotIntel.Core.Models;
using PilotIntel.Core.Style;
using PilotIntel.Integration.zKill;
using PilotIntel.Shared;
using PilotIntel.Shared.Formatting;
using PilotIntel.Shared.zKill;

namespace PilotIntel.UI.ViewModels;

public static class PilotReportRowFactory
{
    public static PilotReportRow FromPilot(
        Pilot pilot,
        zKillActivity? activity,
        zKillStatistics? statistics,
        StyleClassification recentStyle,
        string threatBand)
    {
        var generalStyle = GeneralStyleClassifier.Classify(statistics);

        return new PilotReportRow
        {
            Pilot = GetPilotName(pilot),
            Verify = GetVerifyDisplay(pilot.VerifyStatus),
            Threat = string.IsNullOrWhiteSpace(threatBand)
                ? "Unk"
                : threatBand,
            SecurityStatus = pilot.SecurityStatus?.ToString("0.00") ?? "unk",
            Group = "unk",
            Corporation = pilot.Corporation?.Name ?? "unk",
            Alliance = pilot.Alliance?.Name ?? "None",
            GeneralStyle = StyleDisplayFormatter.Format(generalStyle),
            RecentStyle = StyleDisplayFormatter.Format(recentStyle),
            KillsWeek = FormatActivityValue(activity?.HasPublicActivityData, activity?.KillsWeek),
            SoloWeek = FormatActivityValue(activity?.HasPublicActivityData, activity?.SoloWeek),
            LastActive = FormatLastActive(activity),
            Notes = GetNotes(activity, statistics)
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

    private static string FormatActivityValue(bool? hasPublicActivityData, int? value)
    {
        if (hasPublicActivityData != true)
            return "-";

        return value?.ToString() ?? "-";
    }

    private static string FormatLastActive(zKillActivity? activity)
    {
        if (activity?.HasPublicActivityData != true)
            return "-";

        return LastActiveFormatter.Format(activity.LastActiveUtc, activity.LastActivityType?.ToString());
    }

    private static string GetNotes(zKillActivity? activity, zKillStatistics? statistics)
    {
        if (activity is null)
            return "ESI identity loaded; zKill activity not loaded.";

        if (!string.IsNullOrWhiteSpace(activity.Error))
            return "ESI identity loaded; zKill unavailable.";

        if (!activity.HasPublicActivityData)
            return statistics is null
                ? "ESI identity loaded; no public zKill activity or stats available."
                : "ESI identity loaded; zKill stats loaded; no recent activity.";

        return $"Public zKill activity checked {activity.CheckedAtUtc:yyyy-MM-dd HH:mm} UTC.";
    }
}
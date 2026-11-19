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
        var affiliationFailed = HasAffiliationFailed(pilot);
        var lastKillAge = GetLastKillAge(activity);
        var hasActivity = activity?.HasPublicActivityData == true;

        return new PilotReportRow
        {
            CharacterId = pilot.CharacterId,
            InputName = pilot.InputName,
            Pilot = GetPilotName(pilot),
            EngineAnalysisFailed = engineFailureReason is not null,
            DataRetrievalFailed = affiliationFailed || statisticsCallFailed || recentCallFailed || engineFailureReason is not null,
            Verify = GetVerifyDisplay(pilot.VerifyStatus),
            Threat = string.IsNullOrWhiteSpace(threatBand)
                ? UiText.ThreatBandUnk
                : threatBand,
            SecurityStatus = SecurityStatusFormatter.Format(pilot.SecurityStatus),
            Group = UiText.PlaceholderUnkLower,
            CorporationId = pilot.Corporation?.CorporationId,
            Corporation = pilot.Corporation?.Name ?? UiText.PlaceholderUnkLower,
            CorporationPlain = pilot.Corporation?.Name ?? UiText.PlaceholderUnkLower,
            AllianceId = pilot.Alliance?.AllianceId,
            Alliance = GetAllianceDisplay(pilot),
            AlliancePlain = GetAllianceDisplay(pilot),
            FactionId = pilot.FactionId,
            FactionWarfare = FormatFactionWarfare(pilot.FactionId, 1),
            Style = $"{StyleLetterCodeFormatter.FormatGeneral(generalResult.Classification, generalResult.IsPodder)}/{StyleLetterCodeFormatter.FormatRecent(recentStyle, recentIsPodder)}",
            GeneralStyle = StyleDisplayFormatter.Format(generalResult.Classification, generalResult.IsPodder),
            RecentStyle = StyleDisplayFormatter.Format(recentStyle, recentIsPodder),
            GeneralStyleClassification = generalResult.Classification,
            RecentStyleClassification = recentStyle,
            Week = $"{FormatWeekSideValue(activity?.HasPublicActivityData, activity?.KillsWeek)}/{FormatWeekSideValue(activity?.HasPublicActivityData, activity?.SoloWeek)}",
            Kills = FormatActivityValue(activity?.HasPublicActivityData, activity?.KillsWeek),
            Solos = FormatActivityValue(activity?.HasPublicActivityData, activity?.SoloWeek),
            LastKill = lastKillAge is { } age ? FormatKillAge(age) : UiText.PlaceholderDash,
            InfoWeekKills = FormatWeekSideValue(activity?.HasPublicActivityData, activity?.KillsWeek),
            InfoWeekSolos = FormatWeekSideValue(activity?.HasPublicActivityData, activity?.SoloWeek),
            InfoWeekLosses = FormatInfoCount(derivedActivity?.InfoWeekLosses),
            Notes = GetNotes(affiliationFailed, statisticsCallFailed, recentCallFailed, engineFailureReason is not null),
            ThreatSortKey = PilotGridSortKeys.Threat(threatBand),
            SecuritySortKey = PilotGridSortKeys.Security(pilot.SecurityStatus),
            WeekSortKey = PilotGridSortKeys.Week(hasActivity ? activity!.KillsWeek : null, hasActivity ? activity!.SoloWeek : null),
            LastKillSortKey = PilotGridSortKeys.LastKill(lastKillAge),
            StatsFailureSource = GetStatsFailureSource(affiliationFailed, statisticsCallFailed, recentCallFailed, engineFailureReason)
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
            VerifyStatus.Verified => UiText.GridVerifyVerified,
            VerifyStatus.Partial => UiText.GridVerifyPartial,
            _ => UiText.PlaceholderUnkLower
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

    private static TimeSpan? GetLastKillAge(zKillActivity? activity) =>
        activity?.LastKillUtc is { } lastKillUtc ? ApplicationClock.UtcNow - lastKillUtc : null;

    internal static bool HasAffiliationFailed(Pilot pilot) =>
        pilot.VerifyStatus == VerifyStatus.Failed
        || (pilot.CharacterId is not null && pilot.CorporationId is null && pilot.Corporation is null);

    internal static string FormatKillAge(TimeSpan age)
    {
        if (age.TotalHours < 24)
            return UiText.FormatLastKillHours(Math.Max(1, (int)Math.Ceiling(age.TotalHours)));

        if (age.TotalDays < 30)
            return UiText.FormatLastKillDays((int)age.TotalDays);

        return UiText.GridLastKillOver;
    }

    internal static string GetNotes(bool affiliationFailed, bool statisticsCallFailed, bool recentCallFailed, bool engineFailed)
    {
        var failedCalls = new List<string>();

        if (affiliationFailed)
            failedCalls.Add(UiText.NotesCallEsi);

        if (statisticsCallFailed)
            failedCalls.Add(UiText.NotesCallStatistics);

        if (recentCallFailed)
            failedCalls.Add(UiText.NotesCallRecent);

        if (engineFailed)
            failedCalls.Add(UiText.NotesCallEngine);

        return string.Join(UiText.NotesSeparator, failedCalls);
    }

    private static string? GetStatsFailureSource(
        bool affiliationFailed,
        bool statisticsCallFailed,
        bool recentCallFailed,
        string? engineFailureReason)
    {
        var sources = new List<string>();

        if (affiliationFailed)
            sources.Add(UiText.InfoSheetSourceEsi);

        if (statisticsCallFailed)
            sources.Add(UiText.InfoSheetSourceStatistics);

        if (recentCallFailed)
            sources.Add(UiText.InfoSheetSourceRecent);

        if (engineFailureReason is not null)
            sources.Add(UiText.InfoSheetSourceEngine);

        return sources.Count == 0 ? null : string.Join(UiText.NotesSeparator, sources);
    }
}

using System.Globalization;
using Killright.UI.Configuration;
using Killright.UI.Resources;

namespace Killright.UI.Shortcuts;

public sealed record F1ShortcutEntry(string Key, string Action);

public sealed record F1ShortcutGroup(string Title, IReadOnlyList<F1ShortcutEntry> Entries);

public sealed class F1HelpContent
{
    private F1HelpContent()
    {
    }

    public IReadOnlyList<F1ShortcutGroup> ShortcutGroups { get; private init; } = Array.Empty<F1ShortcutGroup>();

    public string StyleGang { get; private init; } = string.Empty;

    public string StyleBlob { get; private init; } = string.Empty;

    public string StyleFleet { get; private init; } = string.Empty;

    public string ThreatIntro { get; private init; } = string.Empty;

    public string ThreatNone { get; private init; } = string.Empty;

    public string ThreatLow { get; private init; } = string.Empty;

    public string ThreatMedium { get; private init; } = string.Empty;

    public string ThreatHigh { get; private init; } = string.Empty;

    public string ThreatVeryHigh { get; private init; } = string.Empty;

    public string ThreatExtreme { get; private init; } = string.Empty;

    public string RelationshipIntro { get; private init; } = string.Empty;

    public string RelationshipLow { get; private init; } = string.Empty;

    public string RelationshipMedium { get; private init; } = string.Empty;

    public string RelationshipHigh { get; private init; } = string.Empty;

    public string GroupIntro { get; private init; } = string.Empty;

    public string GroupDirect { get; private init; } = string.Empty;

    public string GroupChain { get; private init; } = string.Empty;

    public string AboutVersion { get; private init; } = string.Empty;

    public static F1HelpContent Create(ApplicationSettings settings, string version)
    {
        var threatBands = settings.ThreatBands;
        var relationshipBands = settings.RelationshipConfidenceBands;
        var scoreMaximum = threatBands.Count == 0 ? 0 : threatBands[^1].MaximumScore;

        return new F1HelpContent
        {
            ShortcutGroups = BuildShortcutGroups(),
            StyleGang = Format(UiText.ShortcutsStyleSizeGang, Number(settings.Style.BlobMinimumAverageAttackers)),
            StyleBlob = Format(
                UiText.ShortcutsStyleSizeBlob,
                Number(settings.Style.BlobMinimumAverageAttackers),
                Number(settings.Style.FleetMinimumAverageAttackers)),
            StyleFleet = Format(UiText.ShortcutsStyleSizeFleet, Number(settings.Style.FleetMinimumAverageAttackers)),
            ThreatIntro = Format(UiText.ShortcutsThreatIntro, Number(scoreMaximum)),
            ThreatNone = UiText.ShortcutsThreatNone,
            ThreatLow = BandLine(UiText.ShortcutsThreatLow, threatBands, 0),
            ThreatMedium = BandLine(UiText.ShortcutsThreatMedium, threatBands, 1),
            ThreatHigh = BandLine(UiText.ShortcutsThreatHigh, threatBands, 2),
            ThreatVeryHigh = BandLine(UiText.ShortcutsThreatVeryHigh, threatBands, 3),
            ThreatExtreme = BandLine(UiText.ShortcutsThreatExtreme, threatBands, 4),
            RelationshipIntro = UiText.ShortcutsRelationshipIntro,
            RelationshipLow = BandLine(UiText.ShortcutsRelationshipLow, relationshipBands, 0),
            RelationshipMedium = BandLine(UiText.ShortcutsRelationshipMedium, relationshipBands, 1),
            RelationshipHigh = BandLine(UiText.ShortcutsRelationshipHigh, relationshipBands, 2),
            GroupIntro = UiText.ShortcutsGroupIntro,
            GroupDirect = UiText.ShortcutsGroupDirect,
            GroupChain = UiText.ShortcutsGroupChain,
            AboutVersion = Format(UiText.ShortcutsAboutVersion, version)
        };
    }

    private static IReadOnlyList<F1ShortcutGroup> BuildShortcutGroups() => new[]
    {
        new F1ShortcutGroup(
            UiText.ShortcutsGroupWindow,
            new[]
            {
                new F1ShortcutEntry(UiText.ShortcutsKeyMoveWindow, UiText.ShortcutsActionMoveWindow),
                new F1ShortcutEntry(UiText.ShortcutsKeyMinimizeWindow, UiText.ShortcutsActionMinimizeWindow)
            }),
        new F1ShortcutGroup(
            UiText.ShortcutsGroupFilters,
            new[]
            {
                new F1ShortcutEntry(UiText.ShortcutsKeyClearFilters, UiText.ShortcutsActionClearFilters),
                new F1ShortcutEntry(UiText.ShortcutsKeyPilotFilter, UiText.ShortcutsActionPilotFilter)
            }),
        new F1ShortcutGroup(
            UiText.ShortcutsGroupGeneral,
            new[]
            {
                new F1ShortcutEntry(UiText.ShortcutsKeyOpenMenu, UiText.ShortcutsActionOpenMenu),
                new F1ShortcutEntry(UiText.ShortcutsKeyQuit, UiText.ShortcutsActionQuit)
            })
    };

    private static string BandLine(string template, IReadOnlyList<ThreatBandSetting> bands, int index) =>
        index < bands.Count
            ? Format(template, Number(bands[index].MinimumScore), Number(bands[index].MaximumScore))
            : Format(template, string.Empty, string.Empty);

    private static string BandLine(string template, IReadOnlyList<RelationshipConfidenceBandSetting> bands, int index) =>
        index < bands.Count
            ? Format(template, Number(bands[index].MinimumScore), Number(bands[index].MaximumScore))
            : Format(template, string.Empty, string.Empty);

    private static string Format(string template, params object[] arguments) =>
        string.Format(CultureInfo.InvariantCulture, template, arguments);

    private static string Number(double value) => value.ToString(UiText.ShortcutsNumberFormat, CultureInfo.InvariantCulture);
}

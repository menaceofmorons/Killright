using System.Globalization;
using Killright.UI.Configuration;
using Killright.UI.Resources;

namespace Killright.UI.Shortcuts;

public sealed record F1ShortcutEntry(string Key, string Action);

public sealed record F1ShortcutGroup(string Title, IReadOnlyList<F1ShortcutEntry> Entries);

public sealed class F1HelpContent
{
    private const double GangMinimumAttackers = 2;

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

    public string RelationshipShown { get; private init; } = string.Empty;

    public string RelationshipNumber { get; private init; } = string.Empty;

    public string RelationshipNumberCode { get; private init; } = string.Empty;

    public string RelationshipLow { get; private init; } = string.Empty;

    public string RelationshipMedium { get; private init; } = string.Empty;

    public string RelationshipHigh { get; private init; } = string.Empty;

    public string GroupIntro { get; private init; } = string.Empty;

    public string GroupDirect { get; private init; } = string.Empty;

    public string GroupChain { get; private init; } = string.Empty;

    public string AboutVersion { get; private init; } = string.Empty;

    public static F1HelpContent Create(ApplicationSettings settings, string version)
    {
        var style = settings.Style;

        return new F1HelpContent
        {
            ShortcutGroups = BuildShortcutGroups(),
            StyleFleet = Format(UiText.ShortcutsStyleSizeFleet, Number(style.FleetMinimumAverageAttackers)),
            StyleBlob = Format(
                UiText.ShortcutsStyleSizeBlob,
                Number(style.BlobMinimumAverageAttackers),
                Number(style.FleetMinimumAverageAttackers - 1)),
            StyleGang = Format(
                UiText.ShortcutsStyleSizeGang,
                Number(GangMinimumAttackers),
                Number(style.BlobMinimumAverageAttackers - 1)),
            ThreatIntro = UiText.ShortcutsThreatIntro,
            ThreatNone = UiText.ShortcutsThreatNone,
            ThreatLow = UiText.ShortcutsThreatLow,
            ThreatMedium = UiText.ShortcutsThreatMedium,
            ThreatHigh = UiText.ShortcutsThreatHigh,
            ThreatVeryHigh = UiText.ShortcutsThreatVeryHigh,
            ThreatExtreme = UiText.ShortcutsThreatExtreme,
            RelationshipIntro = UiText.ShortcutsRelationshipIntro,
            RelationshipShown = UiText.ShortcutsRelationshipShown,
            RelationshipNumber = UiText.ShortcutsRelationshipNumber,
            RelationshipNumberCode = UiText.ShortcutsRelationshipNumberCode,
            RelationshipLow =UiText.ShortcutsRelationshipLow,
            RelationshipMedium = UiText.ShortcutsRelationshipMedium,
            RelationshipHigh = UiText.ShortcutsRelationshipHigh,
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
                new F1ShortcutEntry(UiText.ShortcutsKeyPilotFilter, UiText.ShortcutsActionPilotFilter),
                new F1ShortcutEntry(UiText.ShortcutsKeyPilotHold, UiText.ShortcutsActionPilotHold)
            }),
        new F1ShortcutGroup(
            UiText.ShortcutsGroupGeneral,
            new[]
            {
                new F1ShortcutEntry(UiText.ShortcutsKeyOpenMenu, UiText.ShortcutsActionOpenMenu),
                new F1ShortcutEntry(UiText.ShortcutsKeyQuit, UiText.ShortcutsActionQuit)
            })
    };

    private static string Format(string template, params object[] arguments) =>
        string.Format(CultureInfo.InvariantCulture, template, arguments);

    private static string Number(double value) => value.ToString(UiText.ShortcutsNumberFormat, CultureInfo.InvariantCulture);
}

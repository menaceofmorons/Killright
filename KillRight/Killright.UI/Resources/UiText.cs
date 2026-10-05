using System.Globalization;

namespace Killright.UI.Resources;

public static class UiText
{
    public static string WindowTitleMain => Strings.Get("Window.Title.Main");
    public static string GridColumnHeaderThreat => Strings.Get("Grid.ColumnHeader.Threat");
    public static string GridColumnHeaderPilot => Strings.Get("Grid.ColumnHeader.Pilot");
    public static string GridColumnHeaderRelationship => Strings.Get("Grid.ColumnHeader.Relationship");
    public static string GridColumnHeaderStyle => Strings.Get("Grid.ColumnHeader.Style");
    public static string GridColumnHeaderSec => Strings.Get("Grid.ColumnHeader.Sec");
    public static string GridColumnHeaderWeek => Strings.Get("Grid.ColumnHeader.Week");
    public static string GridColumnHeaderCorporation => Strings.Get("Grid.ColumnHeader.Corporation");
    public static string GridColumnHeaderAlliance => Strings.Get("Grid.ColumnHeader.Alliance");
    public static string GridColumnHeaderLastKill => Strings.Get("Grid.ColumnHeader.LastKill");
    public static string GridColumnHeaderNotes => Strings.Get("Grid.ColumnHeader.Notes");
    public static string GridColumnHeaderVerify => Strings.Get("Grid.ColumnHeader.Verify");
    public static string GridColumnHeaderGroup => Strings.Get("Grid.ColumnHeader.Group");
    public static string GridContextMenuHide => Strings.Get("Grid.ContextMenu.Hide");

    public static string ColumnsLabelThreat => Strings.Get("Columns.Label.Threat");
    public static string ColumnsLabelPilot => Strings.Get("Columns.Label.Pilot");
    public static string ColumnsLabelRelationship => Strings.Get("Columns.Label.Relationship");
    public static string ColumnsLabelStyle => Strings.Get("Columns.Label.Style");
    public static string ColumnsLabelSec => Strings.Get("Columns.Label.Sec");
    public static string ColumnsLabelWeek => Strings.Get("Columns.Label.Week");
    public static string ColumnsLabelGroup => Strings.Get("Columns.Label.Group");
    public static string ColumnsLabelCorporation => Strings.Get("Columns.Label.Corporation");
    public static string ColumnsLabelAlliance => Strings.Get("Columns.Label.Alliance");
    public static string ColumnsLabelLastKill => Strings.Get("Columns.Label.LastKill");
    public static string ColumnsLabelNotes => Strings.Get("Columns.Label.Notes");
    public static string ColumnsLabelVerify => Strings.Get("Columns.Label.Verify");

    public static string WindowTitleMenu => Strings.Get("Window.Title.Menu");
    public static string MenuButtonOk => Strings.Get("Menu.Button.Ok");
    public static string MenuButtonCancel => Strings.Get("Menu.Button.Cancel");
    public static string MenuTabConfig => Strings.Get("Menu.Tab.Config");
    public static string MenuConfigAlwaysOnTop => Strings.Get("Menu.Config.AlwaysOnTop");
    public static string MenuConfigSkipBackupOnClose => Strings.Get("Menu.Config.SkipBackupOnClose");
    public static string MenuConfigDefaultSectionTitle => Strings.Get("Menu.Config.DefaultSectionTitle");
    public static string MenuConfigSave => Strings.Get("Menu.Config.Save");
    public static string MenuConfigResetMine => Strings.Get("Menu.Config.ResetMine");
    public static string MenuConfigResetSystem => Strings.Get("Menu.Config.ResetSystem");
    public static string MenuTabAppearance => Strings.Get("Menu.Tab.Appearance");
    public static string MenuAppearanceTheme => Strings.Get("Menu.Appearance.Theme");
    public static string MenuAppearanceThemeLight => Strings.Get("Menu.Appearance.ThemeLight");
    public static string MenuAppearanceThemeDark => Strings.Get("Menu.Appearance.ThemeDark");
    public static string MenuAppearanceThemeFollowWindows => Strings.Get("Menu.Appearance.ThemeFollowWindows");
    public static string MenuAppearanceGridFontSize => Strings.Get("Menu.Appearance.GridFontSize");
    public static string MenuAppearanceFontSizeLarge => Strings.Get("Menu.Appearance.FontSizeLarge");
    public static string MenuAppearanceFontSizeMedium => Strings.Get("Menu.Appearance.FontSizeMedium");
    public static string MenuAppearanceFontSizeSmall => Strings.Get("Menu.Appearance.FontSizeSmall");
    public static string MenuAppearanceFontSizeTiny => Strings.Get("Menu.Appearance.FontSizeTiny");
    public static string MenuAppearanceHighlightColours => Strings.Get("Menu.Appearance.HighlightColours");
    public static string MenuAppearanceHighlightPilot => Strings.Get("Menu.Appearance.HighlightPilot");
    public static string MenuAppearanceHighlightRelated => Strings.Get("Menu.Appearance.HighlightRelated");
    public static string MenuAppearanceHighlightNewPilot => Strings.Get("Menu.Appearance.HighlightNewPilot");
    public static string MenuAppearanceHighlightNewPilotDefault => Strings.Get("Menu.Appearance.HighlightNewPilotDefault");
    public static string MenuTabColumns => Strings.Get("Menu.Tab.Columns");
    public static string MenuTabIgnoreList => Strings.Get("Menu.Tab.IgnoreList");
    public static string MenuIgnoreListSectionTitle => Strings.Get("Menu.IgnoreList.SectionTitle");
    public static string MenuIgnoreListColumnName => Strings.Get("Menu.IgnoreList.ColumnName");
    public static string MenuIgnoreListColumnType => Strings.Get("Menu.IgnoreList.ColumnType");
    public static string MenuIgnoreListRemoveButton => Strings.Get("Menu.IgnoreList.RemoveButton");
    public static string MenuIgnoreListTypePilot => Strings.Get("Menu.IgnoreList.TypePilot");
    public static string MenuIgnoreListTypeCorp => Strings.Get("Menu.IgnoreList.TypeCorp");
    public static string MenuIgnoreListTypeAlliance => Strings.Get("Menu.IgnoreList.TypeAlliance");
    public static string MenuIgnoreListAddButton => Strings.Get("Menu.IgnoreList.AddButton");
    public static string MenuIgnoreListListFromGridTitle => Strings.Get("Menu.IgnoreList.ListFromGridTitle");
    public static string MenuIgnoreListGridColumnPilot => Strings.Get("Menu.IgnoreList.GridColumnPilot");
    public static string MenuIgnoreListGridColumnCorp => Strings.Get("Menu.IgnoreList.GridColumnCorp");
    public static string MenuIgnoreListGridColumnAlliance => Strings.Get("Menu.IgnoreList.GridColumnAlliance");
    public static string MenuWarningCaption => Strings.Get("Menu.Warning.Caption");
    public static string MenuWarningSkipBackupOnClose => Strings.Get("Menu.Warning.SkipBackupOnClose");
    public static string MenuWarningDeveloperTabReveal => Strings.Get("Menu.Warning.DeveloperTabReveal");

    public static string FormatEsiUnresolved(string name, string type) =>
        string.Format(CultureInfo.CurrentCulture, Strings.Get("Menu.IgnoreList.Message.EsiUnresolved"), name, type);

    public static string FormatAlreadyListed(string resolvedName) =>
        string.Format(CultureInfo.CurrentCulture, Strings.Get("Menu.IgnoreList.Message.AlreadyListed"), resolvedName);

    public static string WindowTitleShortcuts => Strings.Get("Window.Title.Shortcuts");
    public static string ShortcutsButtonClose => Strings.Get("Shortcuts.Button.Close");
    public static string ShortcutsTabShortcuts => Strings.Get("Shortcuts.Tab.Shortcuts");
    public static string ShortcutsKeyOpenMenu => Strings.Get("Shortcuts.Key.OpenMenu");
    public static string ShortcutsActionOpenMenu => Strings.Get("Shortcuts.Action.OpenMenu");
    public static string ShortcutsKeyQuit => Strings.Get("Shortcuts.Key.Quit");
    public static string ShortcutsActionQuit => Strings.Get("Shortcuts.Action.Quit");
    public static string ShortcutsKeyHelp => Strings.Get("Shortcuts.Key.Help");
    public static string ShortcutsActionHelp => Strings.Get("Shortcuts.Action.Help");
    public static string ShortcutsKeyRevealDeveloperTab => Strings.Get("Shortcuts.Key.RevealDeveloperTab");
    public static string ShortcutsActionRevealDeveloperTab => Strings.Get("Shortcuts.Action.RevealDeveloperTab");
    public static string ShortcutsKeyMoveWindow => Strings.Get("Shortcuts.Key.MoveWindow");
    public static string ShortcutsActionMoveWindow => Strings.Get("Shortcuts.Action.MoveWindow");
    public static string ShortcutsKeyMinimizeWindow => Strings.Get("Shortcuts.Key.MinimizeWindow");
    public static string ShortcutsActionMinimizeWindow => Strings.Get("Shortcuts.Action.MinimizeWindow");
    public static string ShortcutsTabStyle => Strings.Get("Shortcuts.Tab.Style");
    public static string ShortcutsStyleCodeBlob => Strings.Get("Shortcuts.StyleCode.Blob");
    public static string ShortcutsStyleWordBlob => Strings.Get("Shortcuts.StyleWord.Blob");
    public static string ShortcutsStyleCodeFleet => Strings.Get("Shortcuts.StyleCode.Fleet");
    public static string ShortcutsStyleWordFleet => Strings.Get("Shortcuts.StyleWord.Fleet");
    public static string ShortcutsStyleCodeGang => Strings.Get("Shortcuts.StyleCode.Gang");
    public static string ShortcutsStyleWordGang => Strings.Get("Shortcuts.StyleWord.Gang");
    public static string ShortcutsStyleCodeInactive => Strings.Get("Shortcuts.StyleCode.Inactive");
    public static string ShortcutsStyleWordInactive => Strings.Get("Shortcuts.StyleWord.Inactive");
    public static string ShortcutsStyleCodeSolo => Strings.Get("Shortcuts.StyleCode.Solo");
    public static string ShortcutsStyleWordSolo => Strings.Get("Shortcuts.StyleWord.Solo");
    public static string ShortcutsStyleCodeUnknown => Strings.Get("Shortcuts.StyleCode.Unknown");
    public static string ShortcutsStyleWordUnknown => Strings.Get("Shortcuts.StyleWord.Unknown");
    public static string ShortcutsStyleCodeVictim => Strings.Get("Shortcuts.StyleCode.Victim");
    public static string ShortcutsStyleWordVictim => Strings.Get("Shortcuts.StyleWord.Victim");
    public static string ShortcutsSuffixBeginnerCode => Strings.Get("Shortcuts.Suffix.BeginnerCode");
    public static string ShortcutsSuffixBeginnerDescription => Strings.Get("Shortcuts.Suffix.BeginnerDescription");
    public static string ShortcutsSuffixPodderCode => Strings.Get("Shortcuts.Suffix.PodderCode");
    public static string ShortcutsSuffixPodderDescription => Strings.Get("Shortcuts.Suffix.PodderDescription");
    public static string ShortcutsTabThreat => Strings.Get("Shortcuts.Tab.Threat");
    public static string ShortcutsTabRelationship => Strings.Get("Shortcuts.Tab.Relationship");

    public static string ThreatBandNone => Strings.Get("ThreatBand.None");
    public static string ThreatBandLow => Strings.Get("ThreatBand.Low");
    public static string ThreatBandMedium => Strings.Get("ThreatBand.Medium");
    public static string ThreatBandHigh => Strings.Get("ThreatBand.High");
    public static string ThreatBandVeryHigh => Strings.Get("ThreatBand.Very High");
    public static string ThreatBandExtreme => Strings.Get("ThreatBand.Extreme");
    public static string ThreatBandUnk => Strings.Get("ThreatBand.Unk");

    public static string RelationshipBandLow => Strings.Get("RelationshipBand.Low");
    public static string RelationshipBandMedium => Strings.Get("RelationshipBand.Medium");
    public static string RelationshipBandHigh => Strings.Get("RelationshipBand.High");

    public static string GetThreatBandDisplay(string? bandName) =>
        string.IsNullOrWhiteSpace(bandName) ? ThreatBandUnk : Strings.Get($"ThreatBand.{bandName}", bandName);

    public static string InfoSheetLabelBirthday => Strings.Get("InfoSheet.Label.Birthday");
    public static string InfoSheetLabelThreat => Strings.Get("InfoSheet.Label.Threat");
    public static string InfoSheetLabelSec => Strings.Get("InfoSheet.Label.Sec");
    public static string InfoSheetLabelKills => Strings.Get("InfoSheet.Label.Kills");
    public static string InfoSheetLabelSolos => Strings.Get("InfoSheet.Label.Solos");
    public static string InfoSheetLabelGeneral => Strings.Get("InfoSheet.Label.General");
    public static string InfoSheetLabelRecent => Strings.Get("InfoSheet.Label.Recent");
    public static string InfoSheetLabelLastActivityTitle => Strings.Get("InfoSheet.Label.LastActivityTitle");
    public static string InfoSheetLabelDateTime => Strings.Get("InfoSheet.Label.DateTime");
    public static string InfoSheetLabelKillLoss => Strings.Get("InfoSheet.Label.KillLoss");
    public static string InfoSheetLabelSystem => Strings.Get("InfoSheet.Label.System");
    public static string InfoSheetLabelShip => Strings.Get("InfoSheet.Label.Ship");
    public static string InfoSheetLabelWeapon => Strings.Get("InfoSheet.Label.Weapon");
    public static string InfoSheetLabelVictim => Strings.Get("InfoSheet.Label.Victim");
    public static string InfoSheetLabelAttackers => Strings.Get("InfoSheet.Label.Attackers");
    public static string InfoSheetLoading => Strings.Get("InfoSheet.Loading");
    public static string InfoSheetSourceBirthday => Strings.Get("InfoSheet.Source.Birthday");
    public static string InfoSheetSourceLastActivity => Strings.Get("InfoSheet.Source.LastActivity");

    public static string FormatStatsTitle(string pilotName) =>
        string.Format(CultureInfo.CurrentCulture, Strings.Get("InfoSheet.StatsTitleFormat"), pilotName);

    public static string FormatErrorLoading(string source) =>
        string.Format(CultureInfo.CurrentCulture, Strings.Get("InfoSheet.ErrorLoadingFormat"), source);

    public static string WindowTitleSdeFirstRunLoad => Strings.Get("Window.Title.SdeFirstRunLoad");
    public static string SdeButtonRetry => Strings.Get("Sde.Button.Retry");
    public static string SdeStageCheckingManifest => Strings.Get("Sde.Stage.CheckingManifest");
    public static string SdeStageDownloading => Strings.Get("Sde.Stage.Downloading");
    public static string SdeStageImporting => Strings.Get("Sde.Stage.Importing");
    public static string SdeLoadFailedTitle => Strings.Get("Sde.LoadFailedTitle");
    public static string SdeFailureManifest => Strings.Get("Sde.Failure.Manifest");
    public static string SdeFailureDownload => Strings.Get("Sde.Failure.Download");
    public static string SdeFailureUnexpected => Strings.Get("Sde.Failure.Unexpected");

    public static string PlaceholderDash => Strings.Get("Placeholder.Dash");
}

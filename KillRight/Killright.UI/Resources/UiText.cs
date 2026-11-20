using System.Globalization;

namespace Killright.UI.Resources;

public static class UiText
{
    public static string StartupFailureTitle => Strings.Get("Startup.Failure.Title");
    public static string StartupFailureSettingsUnreadable => Strings.Get("Startup.Failure.SettingsUnreadable");
    public static string StartupFailureSettingsInvalid => Strings.Get("Startup.Failure.SettingsInvalid");
    public static string StartupFailureEngineSettings => Strings.Get("Startup.Failure.EngineSettings");

    public static string FormatStartupFailure(string reason) =>
        string.Format(CultureInfo.CurrentCulture, Strings.Get("Startup.Failure.Text"), reason);

    public static string UiStateCorrupt => Strings.Get("Message.UiStateCorrupt");
    public static string WindowTitleMain => Strings.Get("Window.Title.Main");
    public static string GridColumnHeaderThreat => Strings.Get("Grid.ColumnHeader.Threat");
    public static string GridColumnHeaderPilot => Strings.Get("Grid.ColumnHeader.Pilot");
    public static string GridColumnHeaderRelationship => Strings.Get("Grid.ColumnHeader.Relationship");
    public static string GridColumnHeaderStyle => Strings.Get("Grid.ColumnHeader.Style");
    public static string GridColumnHeaderSec => Strings.Get("Grid.ColumnHeader.Sec");
    public static string GridColumnHeaderWeek => Strings.Get("Grid.ColumnHeader.Week");
    public static string GridColumnHeaderCorporation => Strings.Get("Grid.ColumnHeader.Corporation");
    public static string GridColumnHeaderAlliance => Strings.Get("Grid.ColumnHeader.Alliance");
    public static string GridColumnHeaderFactionWarfare => Strings.Get("Grid.ColumnHeader.FactionWarfare");
    public static string GridColumnHeaderLastKill => Strings.Get("Grid.ColumnHeader.LastKill");
    public static string GridColumnHeaderNotes => Strings.Get("Grid.ColumnHeader.Notes");
    public static string GridColumnHeaderVerify => Strings.Get("Grid.ColumnHeader.Verify");
    public static string GridColumnHeaderGroup => Strings.Get("Grid.ColumnHeader.Group");
    public static string GridContextMenuHide => Strings.Get("Grid.ContextMenu.Hide");
    public static string GridFilterMenuIgnore => Strings.Get("Grid.Filter.Menu.Ignore");
    public static string GridFilterMenuFilterTo => Strings.Get("Grid.Filter.Menu.FilterTo");
    public static string GridFilterMenuGeneral => Strings.Get("Grid.Filter.Menu.General");
    public static string GridFilterMenuRecent => Strings.Get("Grid.Filter.Menu.Recent");

    public static string ColumnsLabelThreat => Strings.Get("Columns.Label.Threat");
    public static string ColumnsLabelPilot => Strings.Get("Columns.Label.Pilot");
    public static string ColumnsLabelRelationship => Strings.Get("Columns.Label.Relationship");
    public static string ColumnsLabelStyle => Strings.Get("Columns.Label.Style");
    public static string ColumnsLabelSec => Strings.Get("Columns.Label.Sec");
    public static string ColumnsLabelWeek => Strings.Get("Columns.Label.Week");
    public static string ColumnsLabelGroup => Strings.Get("Columns.Label.Group");
    public static string ColumnsLabelCorporation => Strings.Get("Columns.Label.Corporation");
    public static string ColumnsLabelAlliance => Strings.Get("Columns.Label.Alliance");
    public static string ColumnsLabelFactionWarfare => Strings.Get("Columns.Label.FactionWarfare");
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
    public static string ShortcutsKeyMoveWindow => Strings.Get("Shortcuts.Key.MoveWindow");
    public static string ShortcutsActionMoveWindow => Strings.Get("Shortcuts.Action.MoveWindow");
    public static string ShortcutsKeyMinimizeWindow => Strings.Get("Shortcuts.Key.MinimizeWindow");
    public static string ShortcutsActionMinimizeWindow => Strings.Get("Shortcuts.Action.MinimizeWindow");
    public static string ShortcutsKeyClearFilters => Strings.Get("Shortcuts.Key.ClearFilters");
    public static string ShortcutsActionClearFilters => Strings.Get("Shortcuts.Action.ClearFilters");
    public static string ShortcutsKeyPilotFilter => Strings.Get("Shortcuts.Key.PilotFilter");
    public static string ShortcutsActionPilotFilter => Strings.Get("Shortcuts.Action.PilotFilter");
    public static string ShortcutsKeyPilotHold => Strings.Get("Shortcuts.Key.PilotHold");
    public static string ShortcutsActionPilotHold => Strings.Get("Shortcuts.Action.PilotHold");
    public static string ShortcutsTabStyle => Strings.Get("Shortcuts.Tab.Style");
    public static string ShortcutsStyleCodeBlob => Strings.Get("Shortcuts.StyleCode.Blob");
    public static string ShortcutsStyleCodeFleet => Strings.Get("Shortcuts.StyleCode.Fleet");
    public static string ShortcutsStyleCodeGang => Strings.Get("Shortcuts.StyleCode.Gang");
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
    public static string ShortcutsTabGroup => Strings.Get("Shortcuts.Tab.Group");
    public static string ShortcutsTabAbout => Strings.Get("Shortcuts.Tab.About");
    public static string ShortcutsGroupWindow => Strings.Get("Shortcuts.Group.Window");
    public static string ShortcutsGroupFilters => Strings.Get("Shortcuts.Group.Filters");
    public static string ShortcutsGroupGeneral => Strings.Get("Shortcuts.Group.General");
    public static string ShortcutsNumberFormat => Strings.Get("Shortcuts.NumberFormat");
    public static string ShortcutsStyleSizeGang => Strings.Get("Shortcuts.StyleSize.Gang");
    public static string ShortcutsStyleSizeBlob => Strings.Get("Shortcuts.StyleSize.Blob");
    public static string ShortcutsStyleSizeFleet => Strings.Get("Shortcuts.StyleSize.Fleet");
    public static string ShortcutsThreatIntro => Strings.Get("Shortcuts.Threat.Intro");
    public static string ShortcutsThreatNone => Strings.Get("Shortcuts.Threat.None");
    public static string ShortcutsThreatLow => Strings.Get("Shortcuts.Threat.Low");
    public static string ShortcutsThreatMedium => Strings.Get("Shortcuts.Threat.Medium");
    public static string ShortcutsThreatHigh => Strings.Get("Shortcuts.Threat.High");
    public static string ShortcutsThreatVeryHigh => Strings.Get("Shortcuts.Threat.VeryHigh");
    public static string ShortcutsThreatExtreme => Strings.Get("Shortcuts.Threat.Extreme");
    public static string ShortcutsRelationshipIntro => Strings.Get("Shortcuts.Relationship.Intro");
    public static string ShortcutsRelationshipLow => Strings.Get("Shortcuts.Relationship.Low");
    public static string ShortcutsRelationshipMedium => Strings.Get("Shortcuts.Relationship.Medium");
    public static string ShortcutsRelationshipHigh => Strings.Get("Shortcuts.Relationship.High");
    public static string ShortcutsGroupIntro => Strings.Get("Shortcuts.Group.Intro");
    public static string ShortcutsGroupDirect => Strings.Get("Shortcuts.Group.Direct");
    public static string ShortcutsGroupChain => Strings.Get("Shortcuts.Group.Chain");
    public static string ShortcutsAboutVersion => Strings.Get("Shortcuts.About.Version");

    public static string ThreatBandNone => Strings.Get("ThreatBand.None");
    public static string ThreatBandLow => Strings.Get("ThreatBand.1");
    public static string ThreatBandMedium => Strings.Get("ThreatBand.2");
    public static string ThreatBandHigh => Strings.Get("ThreatBand.3");
    public static string ThreatBandVeryHigh => Strings.Get("ThreatBand.4");
    public static string ThreatBandExtreme => Strings.Get("ThreatBand.5");
    public static string ThreatBandUnk => Strings.Get("ThreatBand.Unk");

    public static string RelationshipBandLow => Strings.Get("RelationshipBand.1");
    public static string RelationshipBandMedium => Strings.Get("RelationshipBand.2");
    public static string RelationshipBandHigh => Strings.Get("RelationshipBand.3");

    public static string GetThreatBandDisplay(string? bandName) =>
        string.IsNullOrWhiteSpace(bandName) ? ThreatBandUnk : Strings.Get($"ThreatBand.{bandName}", bandName);

    public static string InfoSheetLabelFaction => Strings.Get("InfoSheet.Label.Faction");
    public static string InfoSheetLabelBirthday => Strings.Get("InfoSheet.Label.Birthday");
    public static string InfoSheetLabelThreat => Strings.Get("InfoSheet.Label.Threat");
    public static string InfoSheetLabelSec => Strings.Get("InfoSheet.Label.Sec");
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
    public static string InfoSheetLabelVerify => Strings.Get("InfoSheet.Label.Verify");
    public static string InfoSheetLabelNotes => Strings.Get("InfoSheet.Label.Notes");
    public static string InfoSheetLoading => Strings.Get("InfoSheet.Loading");
    public static string InfoSheetSourceBirthday => Strings.Get("InfoSheet.Source.Birthday");
    public static string InfoSheetTableWeek => Strings.Get("InfoSheet.Table.Week");
    public static string InfoSheetTableTotal => Strings.Get("InfoSheet.Table.Total");
    public static string InfoSheetTableKills => Strings.Get("InfoSheet.Table.Kills");
    public static string InfoSheetTableSolos => Strings.Get("InfoSheet.Table.Solos");
    public static string InfoSheetTableLosses => Strings.Get("InfoSheet.Table.Losses");
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
    public static string PlaceholderUnk => Strings.Get("Placeholder.Unk");
    public static string PlaceholderUnkLower => Strings.Get("Placeholder.UnkLower");
    public static string PlaceholderUnknownGlyph => Strings.Get("Placeholder.UnknownGlyph");

    public static string GridVerifyVerified => Strings.Get("Grid.Verify.Verified");
    public static string GridVerifyPartial => Strings.Get("Grid.Verify.Partial");
    public static string GridLastKillOver => Strings.Get("Grid.LastKill.Over");
    public static string GridFilterIgnorePrefix => Strings.Get("Grid.Filter.IgnorePrefix");
    public static string GridFilterFilterPrefix => Strings.Get("Grid.Filter.FilterPrefix");

    public static string FormatLastKillHours(int hours) =>
        string.Format(CultureInfo.InvariantCulture, Strings.Get("Grid.LastKill.HoursFormat"), hours);

    public static string FormatLastKillDays(int days) =>
        string.Format(CultureInfo.InvariantCulture, Strings.Get("Grid.LastKill.DaysFormat"), days);

    public static string NotesCallEsi => Strings.Get("Notes.Call.Esi");
    public static string NotesCallStatistics => Strings.Get("Notes.Call.Statistics");
    public static string NotesCallRecent => Strings.Get("Notes.Call.Recent");
    public static string NotesCallEngine => Strings.Get("Notes.Call.Engine");
    public static string NotesSeparator => Strings.Get("Notes.Separator");

    public static string? FactionShortCode(long? factionId)
    {
        if (factionId is not { } id)
            return null;

        var key = $"Faction.Code.{id}";
        var code = Strings.Get(key, string.Empty);

        return code.Length == 0 ? null : code;
    }
    public static string DeveloperControlMinus30 => Strings.Get("Developer.Control.Minus30");
    public static string DeveloperControlMinus7 => Strings.Get("Developer.Control.Minus7");
    public static string DeveloperControlMinus1 => Strings.Get("Developer.Control.Minus1");
    public static string DeveloperControlClockReset => Strings.Get("Developer.Control.ClockReset");
    public static string DeveloperControlPlus1 => Strings.Get("Developer.Control.Plus1");
    public static string DeveloperControlPlus7 => Strings.Get("Developer.Control.Plus7");
    public static string DeveloperControlPlus30 => Strings.Get("Developer.Control.Plus30");
    public static string DeveloperControlRefresh => Strings.Get("Developer.Control.Refresh");
    public static string DeveloperControlRemoveExpired => Strings.Get("Developer.Control.RemoveExpired");
    public static string DeveloperControlCopySummary => Strings.Get("Developer.Control.CopySummary");
    public static string DeveloperControlClearIdentity => Strings.Get("Developer.Control.ClearIdentity");
    public static string DeveloperControlClearActivity => Strings.Get("Developer.Control.ClearActivity");
    public static string DeveloperControlClearKillmail => Strings.Get("Developer.Control.ClearKillmail");
    public static string DeveloperControlClearStatistics => Strings.Get("Developer.Control.ClearStatistics");
    public static string DeveloperControlClearAll => Strings.Get("Developer.Control.ClearAll");
    public static string DeveloperControlTabIdentityCache => Strings.Get("Developer.Control.TabIdentityCache");
    public static string DeveloperControlTabActivityCache => Strings.Get("Developer.Control.TabActivityCache");
    public static string DeveloperControlTabRecentKillmails => Strings.Get("Developer.Control.TabRecentKillmails");
    public static string DeveloperControlTabDiagnostics => Strings.Get("Developer.Control.TabDiagnostics");
    public static string DeveloperControlTabDiagnosticsQueries => Strings.Get("Developer.Control.TabDiagnosticsQueries");
    public static string DeveloperControlTabGroupDetection => Strings.Get("Developer.Control.TabGroupDetection");
    public static string DeveloperControlTabThreatBreakdown => Strings.Get("Developer.Control.TabThreatBreakdown");
    public static string DeveloperControlQueryLabel => Strings.Get("Developer.Control.QueryLabel");
    public static string DeveloperControlRunQuery => Strings.Get("Developer.Control.RunQuery");
    public static string DeveloperControlCopyResults => Strings.Get("Developer.Control.CopyResults");
    public static string DeveloperControlScannedIdsLabel => Strings.Get("Developer.Control.ScannedIdsLabel");
    public static string DeveloperControlRunGroupDetection => Strings.Get("Developer.Control.RunGroupDetection");
    public static string DeveloperControlDirectRelationships => Strings.Get("Developer.Control.DirectRelationships");
    public static string DeveloperControlChains => Strings.Get("Developer.Control.Chains");
    public static string DeveloperControlIntermediaryHubs => Strings.Get("Developer.Control.IntermediaryHubs");
    public static string DeveloperControlCharacterIdLabel => Strings.Get("Developer.Control.CharacterIdLabel");
    public static string DeveloperControlRunThreat => Strings.Get("Developer.Control.RunThreat");
    public static string DeveloperMessageDialogTitle => Strings.Get("Developer.Message.DialogTitle");
    public static string DeveloperMessageEnterScannedIds => Strings.Get("Developer.Message.EnterScannedIds");
    public static string DeveloperMessageEnterCharacterId => Strings.Get("Developer.Message.EnterCharacterId");
    public static string DeveloperMessageConfirmClearIdentity => Strings.Get("Developer.Message.ConfirmClearIdentity");
    public static string DeveloperMessageConfirmClearActivity => Strings.Get("Developer.Message.ConfirmClearActivity");
    public static string DeveloperMessageConfirmClearKillmail => Strings.Get("Developer.Message.ConfirmClearKillmail");
    public static string DeveloperMessageConfirmClearStatistics => Strings.Get("Developer.Message.ConfirmClearStatistics");
    public static string DeveloperMessageConfirmClearAllLocked => Strings.Get("Developer.Message.ConfirmClearAllLocked");
    public static string DeveloperMessageConfirmClearAll => Strings.Get("Developer.Message.ConfirmClearAll");
    public static string DeveloperMessageFailed => Strings.Get("Developer.Message.Failed");
    public static string DeveloperMessageActionRemoveExpired => Strings.Get("Developer.Message.ActionRemoveExpired");
    public static string DeveloperMessageActionGroupDetection => Strings.Get("Developer.Message.ActionGroupDetection");
    public static string DeveloperMessageActionThreat => Strings.Get("Developer.Message.ActionThreat");
    public static string DeveloperMessageActionRefresh => Strings.Get("Developer.Message.ActionRefresh");
    public static string DeveloperMessageActionClock => Strings.Get("Developer.Message.ActionClock");
    public static string DeveloperMessageActionClear => Strings.Get("Developer.Message.ActionClear");
    public static string DeveloperMessageActionCopy => Strings.Get("Developer.Message.ActionCopy");
    public static string DeveloperMessageActionQuery => Strings.Get("Developer.Message.ActionQuery");
}

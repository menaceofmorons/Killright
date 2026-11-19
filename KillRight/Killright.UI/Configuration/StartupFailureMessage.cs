using Killright.UI.Resources;

namespace Killright.UI.Configuration;

public enum StartupFailureReason
{
    SettingsFileUnreadable,
    SettingsInvalid,
    EngineSettingsRejected
}

public static class StartupFailureMessage
{
    public static StartupFailureReason ReasonFor(SettingsLoadResult settingsResult) =>
        settingsResult.FailureKind == SettingsFailureKind.Unreadable
            ? StartupFailureReason.SettingsFileUnreadable
            : StartupFailureReason.SettingsInvalid;

    public static string Title => UiText.StartupFailureTitle;

    public static string TextFor(StartupFailureReason reason) =>
        UiText.FormatStartupFailure(ReasonText(reason));

    private static string ReasonText(StartupFailureReason reason) => reason switch
    {
        StartupFailureReason.SettingsFileUnreadable => UiText.StartupFailureSettingsUnreadable,
        StartupFailureReason.SettingsInvalid => UiText.StartupFailureSettingsInvalid,
        _ => UiText.StartupFailureEngineSettings
    };
}

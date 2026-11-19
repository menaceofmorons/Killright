using Killright.UI.Resources;

namespace Killright.UI.Diagnostics;

public static class DeveloperToolGuard
{
    public static string? Run(Action work, string action, Action<string> record)
    {
        try
        {
            work();
            return null;
        }
        catch (Exception exception)
        {
            return Report(action, exception, record);
        }
    }

    public static async Task<string?> RunAsync(Func<Task> work, string action, Action<string> record, bool offUiThread = false)
    {
        try
        {
            if (offUiThread)
                await Task.Run(work);
            else
                await work();

            return null;
        }
        catch (Exception exception)
        {
            return Report(action, exception, record);
        }
    }

    private static string Report(string action, Exception exception, Action<string> record)
    {
        var message = string.Format(UiText.DeveloperMessageFailed, action, exception.Message);

        try
        {
            record($"Developer tab: {action} failed. {exception}");
        }
        catch (Exception)
        {
            return message;
        }

        return message;
    }
}

namespace Killright.UI.Diagnostics;

public static class BackgroundFailureRecorder
{
    public static async Task RunAsync(Func<Task> work, string description, Action<string> record)
    {
        try
        {
            await work();
        }
        catch (Exception exception)
        {
            record($"{description} failed. {exception}");
        }
    }

    public static void RecordUnobserved(UnobservedTaskExceptionEventArgs e, Action<string> record)
    {
        record($"Unobserved task exception: {e.Exception}");
        e.SetObserved();
    }
}

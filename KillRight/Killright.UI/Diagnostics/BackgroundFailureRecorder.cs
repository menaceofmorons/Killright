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

    public static IDisposable Attach(Action<string> record)
    {
        void Handler(object? sender, UnobservedTaskExceptionEventArgs e) => RecordUnobserved(e, record);

        TaskScheduler.UnobservedTaskException += Handler;

        return new Detacher(() => TaskScheduler.UnobservedTaskException -= Handler);
    }

    public static void RecordUnobserved(UnobservedTaskExceptionEventArgs e, Action<string> record)
    {
        record($"Unobserved task exception: {e.Exception}");
        e.SetObserved();
    }

    private sealed class Detacher(Action detach) : IDisposable
    {
        public void Dispose() => detach();
    }
}

using System.Windows;

namespace Killright.UI.Tests;

internal static class Sta
{
    internal static readonly object Gate = new();

    public static void Run(Action action)
    {
        Exception? failure = null;

        var thread = new Thread(() =>
        {
            try
            {
                lock (Gate)
                {
                    if (Application.Current is null)
                        _ = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                }

                action();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (failure is not null)
            throw new Xunit.Sdk.XunitException(failure.ToString());
    }
}

using System.Runtime.InteropServices;

namespace Killright.UI.Interop;

internal static class WindowStyleInterop
{
    private const int GWL_STYLE = -16;
    private const long WS_MAXIMIZEBOX = 0x00010000L;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    public static void RemoveMaximizeBox(IntPtr windowHandle)
    {
        var style = GetWindowLongPtr(windowHandle, GWL_STYLE).ToInt64();
        style &= ~WS_MAXIMIZEBOX;
        SetWindowLongPtr(windowHandle, GWL_STYLE, new IntPtr(style));
    }
}

using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace Killright.UI.ClipboardMonitoring;

public sealed class ClipboardMonitor : IDisposable
{
    private const int WmClipboardUpdate = 0x031D;
    private readonly HwndSource _source;
    private bool _disposed;

    public event EventHandler? ClipboardChanged;

    public ClipboardMonitor(nint windowHandle)
    {
        _source = HwndSource.FromHwnd(windowHandle) ?? throw new InvalidOperationException("Window handle source not found.");
        _source.AddHook(WndProc);
        AddClipboardFormatListener(windowHandle);
    }

    private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg == WmClipboardUpdate)
        {
            ClipboardChanged?.Invoke(this, EventArgs.Empty);
            handled = true;
        }
        return nint.Zero;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _source.RemoveHook(WndProc);
        RemoveClipboardFormatListener(_source.Handle);
        _disposed = true;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool AddClipboardFormatListener(nint hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RemoveClipboardFormatListener(nint hwnd);
}

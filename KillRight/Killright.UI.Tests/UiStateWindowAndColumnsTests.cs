using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using Killright.UI.ClipboardMonitoring;
using Killright.UI.Resources;
using Killright.UI.UiState;
using Xunit;

namespace Killright.UI.Tests;

public sealed class UiStateWindowAndColumnsTests
{
    private const int WmClipboardUpdate = 0x031D;

    [Fact]
    public void WindowLimits_MatchTheMinimumSizeSettings()
    {
        Assert.Equal(60, WindowLimits.MinimumWidth);
        Assert.Equal(60, WindowLimits.MinimumHeight);
    }

    [Fact]
    public void Gate_PersistsOutsideAPreview()
    {
        Assert.True(new ColumnLayoutPersistenceGate().ShouldPersist);
    }

    [Fact]
    public void Gate_DoesNotPersistWhileAPreviewIsApplied_AndResumesAfter()
    {
        var gate = new ColumnLayoutPersistenceGate();

        using (gate.BeginPreview())
        {
            Assert.False(gate.ShouldPersist);

            using (gate.BeginPreview())
                Assert.False(gate.ShouldPersist);

            Assert.False(gate.ShouldPersist);
        }

        Assert.True(gate.ShouldPersist);
    }

    [Fact]
    public void Gate_DisposingAScopeTwiceDoesNotUnderflow()
    {
        var gate = new ColumnLayoutPersistenceGate();
        var scope = gate.BeginPreview();
        scope.Dispose();
        scope.Dispose();

        using (gate.BeginPreview())
            Assert.False(gate.ShouldPersist);

        Assert.True(gate.ShouldPersist);
    }

    [Fact]
    public void MainWindowXaml_HasNoHardCodedMinimumSize()
    {
        var xaml = ReadUiFile("MainWindow.xaml");

        Assert.DoesNotContain("MinWidth=\"120\"", xaml);
        Assert.DoesNotContain("MinHeight=\"60\"", xaml);
    }

    [Fact]
    public void PreviewThenCancel_WritesNothing_PreviewThenOk_StoresTheColumns()
    {
        Sta.Run(() =>
        {
            var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".json");

            try
            {
                var store = new UiStateStore(path);
                var gate = new ColumnLayoutPersistenceGate();
                var stored = store.Current.Columns;
                var previewed = stored.Select(column => column with { Width = column.Width + 50 }).ToList();

                using (gate.BeginPreview())
                {
                    if (gate.ShouldPersist)
                        store.UpdateColumns(previewed);
                }

                Assert.True(stored.SequenceEqual(store.Current.Columns));
                Assert.False(File.Exists(path));

                using (gate.BeginPreview())
                {
                }

                store.Commit(store.Current with { Columns = previewed });
                Assert.True(File.Exists(path));
                Assert.True(previewed.SequenceEqual(new UiStateStore(path).Current.Columns));
            }
            finally
            {
                File.Delete(path);
            }
        });
    }

    [Fact]
    public void CorruptStateMessage_ComesFromTheResourceSet()
    {
        Assert.Equal(
            "Saved window and display settings could not be read and have been reset to defaults.",
            UiText.UiStateCorrupt);
    }

    [Fact]
    public void ClipboardMonitor_StopsRaisingAfterDispose_AndDisposeIsRepeatable()
    {
        Sta.Run(() =>
        {
            using var source = new HwndSource(new HwndSourceParameters("clipboard-test"));
            var monitor = new ClipboardMonitor(source.Handle);
            var raised = 0;
            monitor.ClipboardChanged += (_, _) => raised++;

            SendMessage(source.Handle, WmClipboardUpdate, nint.Zero, nint.Zero);
            Assert.Equal(1, raised);

            monitor.Dispose();
            monitor.Dispose();

            SendMessage(source.Handle, WmClipboardUpdate, nint.Zero, nint.Zero);
            Assert.Equal(1, raised);
        });
    }

    [DllImport("user32.dll")]
    private static extern nint SendMessage(nint hwnd, int msg, nint wParam, nint lParam);

    private static string ReadUiFile(string name)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "KillRight.sln")))
            directory = directory.Parent;

        return File.ReadAllText(Path.Combine(directory!.FullName, "Killright.UI", name));
    }
}

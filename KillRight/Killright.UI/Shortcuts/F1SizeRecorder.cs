using System.Globalization;
using System.IO;

namespace Killright.UI.Shortcuts;

// TEMP: sizing form for the F1 window; remove with the throwaway file once the recorded size is in settings.json.
public static class F1SizeRecorder
{
    public static string GetDefaultPath()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "KillRight",
            "f1-window-size.txt");
    }

    public static string? Record(double width, double height, string? path = null)
    {
        var target = path ?? GetDefaultPath();

        try
        {
            var directory = Path.GetDirectoryName(target);

            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            var text = string.Create(
                CultureInfo.InvariantCulture,
                $"width={width:0}{Environment.NewLine}height={height:0}{Environment.NewLine}");

            File.WriteAllText(target, text);
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return ex.Message;
        }
    }
}

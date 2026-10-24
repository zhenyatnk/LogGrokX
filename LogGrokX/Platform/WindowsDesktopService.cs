using System.Diagnostics;
using System.IO;
using System.ComponentModel;

namespace LogGrokX.Platform;

public sealed class WindowsDesktopService : IDesktopService
{
    public void OpenUrl(string url) => OpenPath(url);

    public void OpenPath(string path)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Win32Exception e) when (e.NativeErrorCode == 1155)
        {
            using var process = Process.Start(new ProcessStartInfo(path) { UseShellExecute = true, Verb = "openas" });
        }
    }

    public void RevealFile(string path)
    {
        var target = File.Exists(path) ? path : Path.GetDirectoryName(path);
        using var process = Process.Start(new ProcessStartInfo("explorer.exe")
        {
            Arguments = $"/select,\"{target}\"",
            UseShellExecute = true
        });
    }

    public void SetClipboardText(string text) => TextCopy.ClipboardService.SetText(text);
}

using System.Diagnostics;

namespace CursorAgents;

// Hands cursor:// links to the OS, which passes them to Cursor: `open` on macOS,
// the shell's URL handler on Windows.
public static class Opener
{
    public static void Open(string link)
    {
        try
        {
            var start = OperatingSystem.IsWindows()
                ? new ProcessStartInfo(link) { UseShellExecute = true }
                : new ProcessStartInfo("/usr/bin/open", [link]) { UseShellExecute = false };
            using var process = Process.Start(start);
        }
        catch (Exception e)
        {
            Log.Error($"could not open a Cursor link: {e.Message}");
        }
    }
}

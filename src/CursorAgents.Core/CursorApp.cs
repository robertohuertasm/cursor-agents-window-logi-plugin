using System.Diagnostics;

namespace CursorAgents;

public static class CursorApp
{
    public const string BundleId = "com.todesktop.230313mzl4w4u92";

    // The main process; helpers are named "Cursor Helper (…)".
    public static bool IsRunning()
    {
        var processes = Process.GetProcessesByName("Cursor");
        try
        {
            return processes.Length > 0;
        }
        finally
        {
            foreach (var p in processes) p.Dispose();
        }
    }

    // Keyboard shortcuts go to the frontmost app, so they are only sent when that is Cursor.
    // macOS only; elsewhere the check is skipped (untested).
    public static bool IsFrontmost()
    {
        if (!OperatingSystem.IsMacOS()) return true;
        var front = Run("/usr/bin/lsappinfo", "front")?.Trim();
        if (string.IsNullOrEmpty(front)) return false;
        var info = Run("/usr/bin/lsappinfo", "info", "-only", "bundleid", front);
        return info?.Contains(BundleId, StringComparison.Ordinal) == true;
    }

    private static string? Run(string file, params string[] args)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo(file, args) { RedirectStandardOutput = true, UseShellExecute = false });
            if (p is null) return null;
            var output = p.StandardOutput.ReadToEnd();
            p.WaitForExit(2000);
            return output;
        }
        catch (Exception e)
        {
            Log.Warning($"could not run {file}: {e.Message}");
            return null;
        }
    }
}

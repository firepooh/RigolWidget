using System.IO;

namespace RigolWidget.Visa;

/// <summary>
/// Lightweight log for diagnosing communication failures. Writes to %LOCALAPPDATA%\RigolWidget\rigolwidget.log.
/// Only records failures and state transitions, so it stays quiet under normal conditions.
/// </summary>
internal static class DebugLog
{
    private static readonly object Lock = new();
    private static readonly string Path;

    static DebugLog()
    {
        string dir = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RigolWidget");
        try { Directory.CreateDirectory(dir); } catch { /* ignore */ }
        Path = System.IO.Path.Combine(dir, "rigolwidget.log");
    }

    public static void Write(string message)
    {
        try
        {
            lock (Lock)
                File.AppendAllText(Path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {message}\r\n");
        }
        catch { /* ignore logging failures */ }
    }
}

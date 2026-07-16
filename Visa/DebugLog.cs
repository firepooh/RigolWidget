using System.IO;

namespace RigolWidget.Visa;

/// <summary>
/// 통신 실패 진단용 간이 로그. %LOCALAPPDATA%\RigolWidget\rigolwidget.log 에 기록한다.
/// 실패/상태전환만 기록하므로 평상시엔 조용하다.
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
        catch { /* 로그 실패는 무시 */ }
    }
}

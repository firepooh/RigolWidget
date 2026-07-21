using System.IO;
using System.Text.Json;

namespace RigolWidget.Services;

/// <summary>
/// 앱 설정(%APPDATA%\RigolWidget\settings.json). 현재는 내장 MCP 서버 관련 설정만 담는다.
/// 저장 실패는 무시(치명적이지 않음).
/// </summary>
public sealed class AppSettings
{
    // 내장 MCP 서버 사용 여부(기본 꺼짐 — 명시적 활성화 필요)
    public bool McpEnabled { get; set; }
    // MCP 서버가 제어(쓰기) 명령을 허용하는지(기본 꺼짐 — 읽기 전용)
    public bool McpAllowControl { get; set; }
    // MCP 서버 포트(127.0.0.1 바인딩)
    public int McpPort { get; set; } = 7735;

    private static string Dir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RigolWidget");
    private static string FilePath => Path.Combine(Dir, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var s = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath));
                if (s != null)
                {
                    if (s.McpPort is < 1 or > 65535) s.McpPort = 7735;
                    return s;
                }
            }
        }
        catch { /* 손상 시 기본값 */ }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(FilePath,
                JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { /* 저장 실패는 무시 */ }
    }
}

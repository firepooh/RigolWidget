using System.IO;
using System.Text.Json;

namespace RigolWidget.Services;

/// <summary>
/// App settings (%APPDATA%\RigolWidget\settings.json): last device + embedded MCP server settings.
/// Save failures are ignored (not fatal).
/// </summary>
public sealed class AppSettings
{
    // Skip the device-select window and reconnect to LastResource on startup
    public bool AutoConnect { get; set; } = true;
    // VISA resource string of the last connected device (e.g. USB0::0x1AB1::0x0E11::DP8...::INSTR)
    public string? LastResource { get; set; }

    // Whether the embedded MCP server is enabled (off by default — requires explicit activation)
    public bool McpEnabled { get; set; }
    // Whether the MCP server allows control (write) commands (off by default — read-only)
    public bool McpAllowControl { get; set; }
    // MCP server port (bound to 127.0.0.1)
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
        catch { /* fall back to defaults if corrupted */ }
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
        catch { /* ignore save failures */ }
    }
}

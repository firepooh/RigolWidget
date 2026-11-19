using System.IO;
using System.Text.Json;

namespace RigolWidget.Services;

/// <summary>
/// App settings (%APPDATA%\RigolWidget\settings.json): last device, window position, output limits
/// and embedded MCP server settings. Save failures are ignored (not fatal).
/// </summary>
public sealed class AppSettings
{
    /// <summary>User limit for one channel. Null = no limit beyond the model rating.</summary>
    public sealed class ChannelLimit
    {
        public double? MaxVolts { get; set; }
        public double? MaxAmps { get; set; }
    }

    // Last widget position in WPF device-independent units. Null = default placement.
    public double? WindowLeft { get; set; }
    public double? WindowTop { get; set; }

    // User output limits, index 0..2 = CH1..CH3. Enforced for the widget and for MCP;
    // deliberately not changeable through MCP.
    public ChannelLimit[] Limits { get; set; } = NewLimits();

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
                    s.Limits = NormalizeLimits(s.Limits);
                    if (s.WindowLeft is double l && !double.IsFinite(l)) s.WindowLeft = null;
                    if (s.WindowTop is double t && !double.IsFinite(t)) s.WindowTop = null;
                    return s;
                }
            }
        }
        catch { /* fall back to defaults if corrupted */ }
        return new AppSettings();
    }

    private static ChannelLimit[] NewLimits() => new[] { new ChannelLimit(), new ChannelLimit(), new ChannelLimit() };

    /// <summary>Always three entries; drop negative or non-finite values (treated as "no limit").</summary>
    public static ChannelLimit[] NormalizeLimits(ChannelLimit[]? limits)
    {
        var result = NewLimits();
        if (limits == null) return result;
        for (int i = 0; i < result.Length && i < limits.Length; i++)
        {
            if (limits[i] is null) continue;
            result[i].MaxVolts = Valid(limits[i].MaxVolts);
            result[i].MaxAmps = Valid(limits[i].MaxAmps);
        }
        return result;

        static double? Valid(double? v) => v is double d && double.IsFinite(d) && d >= 0 ? d : null;
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

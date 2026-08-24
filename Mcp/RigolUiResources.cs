using System.ComponentModel;
using System.IO;
using System.Reflection;
using ModelContextProtocol.Extensions.Apps;
using ModelContextProtocol.Server;

namespace RigolWidget.Mcp;

/// <summary>
/// MCP Apps resource: the interactive control panel rendered inside the AI client.
/// The HTML is embedded in the assembly so it also works from a single-file build.
/// </summary>
[McpServerResourceType]
public sealed class RigolUiResources
{
    private const string PanelResourceName = "RigolWidget.Mcp.panel.html";
    private static string? _panelHtml;

    [McpServerResource(UriTemplate = "ui://rigol/panel", Name = "rigol-control-panel",
                      MimeType = McpApps.HtmlMimeType)]
    // Note: McpMeta(name, string) serializes the value AS a JSON string; set JsonValue so the
    // host receives _meta.ui as a real JSON object.
    [McpMeta("ui", JsonValue = """{"prefersBorder":true}""")]
    [Description("Interactive RIGOL DP800 control panel: live per-channel measurements, output toggles, and setpoint entry.")]
    public static string GetControlPanel() => _panelHtml ??= ReadEmbedded(PanelResourceName);

    private static string ReadEmbedded(string name)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"Embedded resource not found: {name}");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}

using RigolWidget.Visa;

namespace RigolWidget.Mcp;

/// <summary>
/// Context shared by the MCP tools. Uses the <b>same</b> VISA session (Dp832) as the running widget
/// to control the device without session conflicts. Thread safety is guaranteed by RigolConnection's lock.
/// </summary>
public sealed class RigolMcpContext
{
    private readonly Dp832 _device;

    public RigolMcpContext(Dp832 device) => _device = device;

    public Dp832 Device => _device;

    /// <summary>Whether control (write) is allowed. When off, the tools can only read.</summary>
    public volatile bool ControlAllowed;

    /// <summary>Currently detected model (used for rating clamps). Defaults to DP832.</summary>
    public volatile Dp800Model Model = Dp800Models.Default;

    /// <summary>Called when a write command originates from MCP (for logging and immediate UI sync).</summary>
    public Action<string>? OnCommand;

    public bool IsConnected => _device.IsConnected;

    public void NotifyCommand(string message) => OnCommand?.Invoke(message);
}

using System.ComponentModel;
using System.Globalization;
using ModelContextProtocol.Server;
using RigolWidget.Visa;

namespace RigolWidget.Mcp;

/// <summary>
/// MCP tools to control/query a RIGOL DP800 power supply.
/// Write tools require the context ControlAllowed flag to be on (read-only by default).
/// All setpoints are clamped to the detected model ratings.
/// </summary>
[McpServerToolType]
public sealed class RigolTools
{
    private readonly RigolMcpContext _ctx;

    public RigolTools(RigolMcpContext ctx) => _ctx = ctx;

    // ---- Read ----

    [McpServerTool(Name = "get_identity")]
    [Description("Return the device identity (*IDN?) and the detected model name.")]
    public string GetIdentity()
    {
        if (!_ctx.IsConnected) return Err("Device is not connected.");
        _ctx.Device.TryGetIdentity(out string idn);
        return $"{{\"model\":\"{_ctx.Model.Name}\",\"idn\":\"{idn.Trim()}\"}}";
    }

    [McpServerTool(Name = "get_status")]
    [Description("Return all channels measurements (voltage/current), setpoints, output state, CV/CC mode, and OCP/OVP protection state including trip status.")]
    public object GetStatus()
    {
        if (!_ctx.IsConnected) return new { error = "Device is not connected." };

        var list = new List<object>();
        var model = _ctx.Model;
        int channels = model.HasCh2 ? 2 : 1;
        for (int ch = 1; ch <= channels; ch++)
        {
            _ctx.Device.TryReadMeasurementAll(ch, out double mv, out double ma, out double mw);
            _ctx.Device.TryGetApplied(ch, out double sv, out double sa);
            _ctx.Device.TryGetMode(ch, out string mode);
            _ctx.Device.TryGetOutputState(ch, out bool outOn);
            _ctx.Device.TryGetOcpState(ch, out bool ocp);
            _ctx.Device.TryGetOcpValue(ch, out double ocpVal);
            _ctx.Device.TryGetOcpAlarm(ch, out bool ocpTrip);
            _ctx.Device.TryGetOvpState(ch, out bool ovp);
            _ctx.Device.TryGetOvpValue(ch, out double ovpVal);
            _ctx.Device.TryGetOvpAlarm(ch, out bool ovpTrip);

            list.Add(new
            {
                channel = ch,
                output_on = outOn,
                measured = new { volts = R(mv), amps = R(ma), watts = R(mw) },
                setpoint = new { volts = R(sv), amps = R(sa) },
                mode,
                ocp = new { enabled = ocp, threshold_a = R(ocpVal), tripped = ocp && ocpTrip },
                ovp = new { enabled = ovp, threshold_v = R(ovpVal), tripped = ovp && ovpTrip },
            });
        }
        return new { model = model.Name, control_allowed = _ctx.ControlAllowed, channels = list };
    }

    // ---- Control (write) ----

    [McpServerTool(Name = "set_voltage")]
    [Description("Set the output voltage (V) of the given channel. The value is clamped to the model rating.")]
    public string SetVoltage(
        [Description("Channel number (1 or 2)")] int channel,
        [Description("Voltage to set (V)")] double volts)
    {
        if (!TryGuard(channel, out string err, out var rating)) return err;
        double v = Clamp(volts, 0, rating.VMax);
        bool ok = _ctx.Device.SetVoltage(channel, v);
        _ctx.NotifyCommand($"[MCP] CH{channel} set voltage {F(v)}V");
        return ok ? Ok($"Set CH{channel} voltage to {F(v)}V.") : Err("Send failed (communication error).");
    }

    [McpServerTool(Name = "set_current")]
    [Description("Set the output current limit (A) of the given channel. The value is clamped to the model rating.")]
    public string SetCurrent(
        [Description("Channel number (1 or 2)")] int channel,
        [Description("Current to set (A)")] double amps)
    {
        if (!TryGuard(channel, out string err, out var rating)) return err;
        double a = Clamp(amps, 0, rating.IMax);
        bool ok = _ctx.Device.SetCurrent(channel, a);
        _ctx.NotifyCommand($"[MCP] CH{channel} set current {F(a)}A");
        return ok ? Ok($"Set CH{channel} current limit to {F(a)}A.") : Err("Send failed (communication error).");
    }

    [McpServerTool(Name = "set_output")]
    [Description("Turn the given channel output on or off.")]
    public string SetOutput(
        [Description("Channel number (1 or 2)")] int channel,
        [Description("true = output ON, false = output OFF")] bool on)
    {
        if (!TryGuard(channel, out string err, out _)) return err;
        bool ok = _ctx.Device.SetOutput(channel, on);
        _ctx.NotifyCommand($"[MCP] CH{channel} output {(on ? "ON" : "OFF")}");
        return ok ? Ok($"Turned CH{channel} output {(on ? "on" : "off")}.") : Err("Send failed (communication error).");
    }

    [McpServerTool(Name = "set_ocp")]
    [Description("Enable or disable over-current protection (OCP) for the given channel, optionally setting the threshold current (A).")]
    public string SetOcp(
        [Description("Channel number (1 or 2)")] int channel,
        [Description("true = enable OCP, false = disable")] bool enabled,
        [Description("Threshold current (A). Omit to keep the current value.")] double? amps = null)
    {
        if (!TryGuard(channel, out string err, out var rating)) return err;
        string extra = "";
        if (amps is double a)
        {
            double c = Clamp(a, 0, rating.OcpMax);
            _ctx.Device.SetOcpValue(channel, c);
            extra = $", threshold {F(c)}A";
        }
        bool ok = _ctx.Device.SetOcp(channel, enabled);
        _ctx.NotifyCommand($"[MCP] CH{channel} OCP {(enabled ? "ON" : "OFF")}{extra}");
        return ok ? Ok($"CH{channel} OCP {(enabled ? "enabled" : "disabled")}{extra} done.") : Err("Send failed (communication error).");
    }

    [McpServerTool(Name = "set_ovp")]
    [Description("Enable or disable over-voltage protection (OVP) for the given channel, optionally setting the threshold voltage (V).")]
    public string SetOvp(
        [Description("Channel number (1 or 2)")] int channel,
        [Description("true = enable OVP, false = disable")] bool enabled,
        [Description("Threshold voltage (V). Omit to keep the current value.")] double? volts = null)
    {
        if (!TryGuard(channel, out string err, out var rating)) return err;
        string extra = "";
        if (volts is double v)
        {
            double c = Clamp(v, 0.01, rating.OvpMax);
            _ctx.Device.SetOvpValue(channel, c);
            extra = $", threshold {F(c)}V";
        }
        bool ok = _ctx.Device.SetOvp(channel, enabled);
        _ctx.NotifyCommand($"[MCP] CH{channel} OVP {(enabled ? "ON" : "OFF")}{extra}");
        return ok ? Ok($"CH{channel} OVP {(enabled ? "enabled" : "disabled")}{extra} done.") : Err("Send failed (communication error).");
    }

    [McpServerTool(Name = "clear_trip")]
    [Description("Clear a protection trip (alarm) on the given channel. kind is 'ocp' or 'ovp'.")]
    public string ClearTrip(
        [Description("Channel number (1 or 2)")] int channel,
        [Description("'ocp' (over-current) or 'ovp' (over-voltage)")] string kind)
    {
        if (!TryGuard(channel, out string err, out _)) return err;
        kind = kind?.Trim().ToLowerInvariant() ?? "";
        bool ok;
        if (kind == "ocp") ok = _ctx.Device.ClearOcpAlarm(channel);
        else if (kind == "ovp") ok = _ctx.Device.ClearOvpAlarm(channel);
        else return Err("kind must be 'ocp' or 'ovp'.");
        _ctx.NotifyCommand($"[MCP] CH{channel} {kind.ToUpperInvariant()} trip cleared");
        return ok ? Ok($"Cleared CH{channel} {kind.ToUpperInvariant()} trip.") : Err("Send failed (communication error).");
    }

    // ---- Internal helpers ----

    /// <summary>Validate control-allowed, connection, and channel. On failure, err holds the reason.</summary>
    private bool TryGuard(int channel, out string err, out ChannelRating rating)
    {
        rating = _ctx.Model.RatingFor(channel);
        err = "";
        if (!_ctx.ControlAllowed)
        {
            err = Err("Control is disabled. Enable 'Allow MCP Control' from the widget right-click menu.");
            return false;
        }
        if (!_ctx.IsConnected)
        {
            err = Err("Device is not connected.");
            return false;
        }
        if (channel < 1 || channel > 2 || (channel == 2 && !_ctx.Model.HasCh2))
        {
            err = Err($"Invalid channel: {channel} (this model supports {(_ctx.Model.HasCh2 ? "CH1·CH2" : "CH1")} only).");
            return false;
        }
        return true;
    }

    private static double Clamp(double v, double lo, double hi) => Math.Round(Math.Clamp(v, lo, hi), 3);
    private static double R(double v) => Math.Round(v, 3);
    private static string F(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);
    private static string Ok(string m) => "OK: " + m;
    private static string Err(string m) => "ERROR: " + m;
}

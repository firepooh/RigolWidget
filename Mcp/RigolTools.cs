using System.ComponentModel;
using System.Globalization;
using ModelContextProtocol;
using ModelContextProtocol.Extensions.Apps;
using ModelContextProtocol.Server;
using RigolWidget.Services;
using RigolWidget.Visa;

namespace RigolWidget.Mcp;

/// <summary>
/// MCP tools to control/query a RIGOL DP800 power supply.
/// Write tools require the context ControlAllowed flag to be on (read-only by default).
/// Setpoints above a user limit set in the widget are refused; otherwise they are clamped to the
/// detected model rating. Read tools answer from the widget's poll data while it is fresh.
/// </summary>
[McpServerToolType]
public sealed class RigolTools
{
    private const string ChannelDesc = "Channel number (1, 2, or 3; availability depends on model)";
    private const string PanelUri = "ui://rigol/panel";

    // The poll loop measures every ~1 s and reads all settings every ~4 s; allow some slack.
    private static readonly TimeSpan MaxMeasAge = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan MaxSettingsAge = TimeSpan.FromSeconds(7);

    private readonly RigolMcpContext _ctx;

    public RigolTools(RigolMcpContext ctx) => _ctx = ctx;

    // ---- Read ----
    // ReadOnly: no device state is changed. OpenWorld=false: the target is one local instrument,
    // not an open-ended external service.

    [McpServerTool(Name = "get_identity", Title = "Read device identity",
                   ReadOnly = true, OpenWorld = false, Idempotent = true, UseStructuredContent = true)]
    [Description("Return the device identity (*IDN?) and the detected model name.")]
    public IdentityResult GetIdentity()
    {
        RequireConnection();
        _ctx.Device.TryGetIdentity(out string idn);
        return new IdentityResult(_ctx.Model.Name, idn.Trim(), _ctx.Model.ChannelCount);
    }

    [McpServerTool(Name = "get_status", Title = "Read all channel measurements and settings",
                   ReadOnly = true, OpenWorld = false, Idempotent = true, UseStructuredContent = true)]
    [Description("Return all channels measurements (voltage/current/power), setpoints, output state, CV/CC mode, OCP/OVP protection state including trip status, and the setpoint limits the widget enforces. Served from the widget's live polling when fresh; see source and the age fields.")]
    public StatusResult GetStatus() => ReadStatus();

    [McpServerTool(Name = "show_panel", Title = "Open the live control panel",
                   ReadOnly = true, OpenWorld = false, UseStructuredContent = true)]
    [McpAppUi(ResourceUri = PanelUri)]
    [Description("Open an interactive control panel showing live measurements for every channel, with output toggles, setpoint entry and an all-off button. Returns the same data as get_status.")]
    public StatusResult ShowPanel() => ReadStatus();

    // ---- Control (write) ----
    // Destructive: these change the physical output and can damage a device under test.
    // Idempotent: sending the same call twice leaves the instrument in the same state.

    [McpServerTool(Name = "set_voltage", Title = "Set channel voltage",
                   Destructive = true, Idempotent = true, OpenWorld = false)]
    [Description("Set the output voltage (V) of the given channel. A value above the user limit set in the widget is refused (see limits in get_status); otherwise it is clamped to the model rating.")]
    public string SetVoltage(
        [Description(ChannelDesc)] int channel,
        [Description("Voltage to set (V)")] double volts)
    {
        var rating = Guard(channel);
        if (_ctx.Limits.ExceedsVolts(channel, volts, out double limit))
            throw new McpException($"CH{channel}: {F(volts)} V is above the {F(limit)} V limit set in the widget. Not applied.");

        double v = Clamp(volts, 0, rating.VMax);
        bool ok = _ctx.Device.SetVoltage(channel, v);
        _ctx.NotifyCommand($"[MCP] CH{channel} set voltage {F(v)}V");
        return ok ? Ok($"Set CH{channel} voltage to {F(v)}V.") : throw SendFailed();
    }

    [McpServerTool(Name = "set_current", Title = "Set channel current limit",
                   Destructive = true, Idempotent = true, OpenWorld = false)]
    [Description("Set the output current limit (A) of the given channel. A value above the user limit set in the widget is refused (see limits in get_status); otherwise it is clamped to the model rating.")]
    public string SetCurrent(
        [Description(ChannelDesc)] int channel,
        [Description("Current to set (A)")] double amps)
    {
        var rating = Guard(channel);
        if (_ctx.Limits.ExceedsAmps(channel, amps, out double limit))
            throw new McpException($"CH{channel}: {F(amps)} A is above the {F(limit)} A limit set in the widget. Not applied.");

        double a = Clamp(amps, 0, rating.IMax);
        bool ok = _ctx.Device.SetCurrent(channel, a);
        _ctx.NotifyCommand($"[MCP] CH{channel} set current {F(a)}A");
        return ok ? Ok($"Set CH{channel} current limit to {F(a)}A.") : throw SendFailed();
    }

    [McpServerTool(Name = "set_output", Title = "Turn channel output on/off",
                   Destructive = true, Idempotent = true, OpenWorld = false)]
    [Description("Turn the given channel output on or off. Turning on is refused while the channel's setpoint is above a user limit set in the widget.")]
    public string SetOutput(
        [Description(ChannelDesc)] int channel,
        [Description("true = output ON, false = output OFF")] bool on)
    {
        Guard(channel);
        if (on && _ctx.Limits.HasUserLimit(channel))
        {
            // Check the setpoint the instrument actually holds (the front panel may have changed it).
            if (!_ctx.Device.TryGetApplied(channel, out double setV, out double setA))
                throw new McpException($"Could not read the CH{channel} setpoint to check the limit. Output not turned on.");
            if (_ctx.Limits.CheckOutputOn(channel, setV, setA) is string reason)
                throw new McpException(reason + " Lower the setpoint first. Output not turned on.");
        }

        bool ok = _ctx.Device.SetOutput(channel, on);
        _ctx.NotifyCommand($"[MCP] CH{channel} output {(on ? "ON" : "OFF")}");
        return ok ? Ok($"Turned CH{channel} output {(on ? "on" : "off")}.") : throw SendFailed();
    }

    [McpServerTool(Name = "all_outputs_off", Title = "Turn every output off",
                   Destructive = true, Idempotent = true, OpenWorld = false)]
    [Description("Turn off the output of every channel at once (emergency stop). Requires 'Allow MCP Control' like the other write tools; the widget's own All OFF button always works.")]
    public string AllOutputsOff()
    {
        RequireControl();
        RequireConnection();

        int count = _ctx.Model.ChannelCount;
        var failed = new List<string>();
        for (int ch = 1; ch <= count; ch++)
            if (!_ctx.Device.SetOutput(ch, false)) failed.Add("CH" + ch);

        _ctx.NotifyCommand("[MCP] all outputs OFF");
        if (failed.Count > 0)
            throw new McpException($"Could not turn off {string.Join(", ", failed)} (communication error).");
        return Ok(count == 1 ? "Turned CH1 output off." : $"Turned off all outputs (CH1..CH{count}).");
    }

    [McpServerTool(Name = "set_ocp", Title = "Configure over-current protection",
                   Destructive = true, Idempotent = true, OpenWorld = false)]
    [Description("Enable or disable over-current protection (OCP) for the given channel, optionally setting the threshold current (A).")]
    public string SetOcp(
        [Description(ChannelDesc)] int channel,
        [Description("true = enable OCP, false = disable")] bool enabled,
        [Description("Threshold current (A). Omit to keep the current value.")] double? amps = null)
    {
        var rating = Guard(channel);
        string extra = "";
        if (amps is double a)
        {
            double c = Clamp(a, 0, rating.OcpMax);
            _ctx.Device.SetOcpValue(channel, c);
            extra = $", threshold {F(c)}A";
        }
        bool ok = _ctx.Device.SetOcp(channel, enabled);
        _ctx.NotifyCommand($"[MCP] CH{channel} OCP {(enabled ? "ON" : "OFF")}{extra}");
        return ok ? Ok($"CH{channel} OCP {(enabled ? "enabled" : "disabled")}{extra} done.") : throw SendFailed();
    }

    [McpServerTool(Name = "set_ovp", Title = "Configure over-voltage protection",
                   Destructive = true, Idempotent = true, OpenWorld = false)]
    [Description("Enable or disable over-voltage protection (OVP) for the given channel, optionally setting the threshold voltage (V).")]
    public string SetOvp(
        [Description(ChannelDesc)] int channel,
        [Description("true = enable OVP, false = disable")] bool enabled,
        [Description("Threshold voltage (V). Omit to keep the current value.")] double? volts = null)
    {
        var rating = Guard(channel);
        string extra = "";
        if (volts is double v)
        {
            double c = Clamp(v, 0.01, rating.OvpMax);
            _ctx.Device.SetOvpValue(channel, c);
            extra = $", threshold {F(c)}V";
        }
        bool ok = _ctx.Device.SetOvp(channel, enabled);
        _ctx.NotifyCommand($"[MCP] CH{channel} OVP {(enabled ? "ON" : "OFF")}{extra}");
        return ok ? Ok($"CH{channel} OVP {(enabled ? "enabled" : "disabled")}{extra} done.") : throw SendFailed();
    }

    [McpServerTool(Name = "clear_trip", Title = "Clear a protection trip",
                   Destructive = true, Idempotent = true, OpenWorld = false)]
    [Description("Clear a protection trip (alarm) on the given channel. kind is 'ocp' or 'ovp'.")]
    public string ClearTrip(
        [Description(ChannelDesc)] int channel,
        [Description("'ocp' (over-current) or 'ovp' (over-voltage)")] string kind)
    {
        Guard(channel);
        kind = kind?.Trim().ToLowerInvariant() ?? "";
        bool ok;
        if (kind == "ocp") ok = _ctx.Device.ClearOcpAlarm(channel);
        else if (kind == "ovp") ok = _ctx.Device.ClearOvpAlarm(channel);
        else throw new McpException("kind must be 'ocp' or 'ovp'.");
        _ctx.NotifyCommand($"[MCP] CH{channel} {kind.ToUpperInvariant()} trip cleared");
        return ok ? Ok($"Cleared CH{channel} {kind.ToUpperInvariant()} trip.") : throw SendFailed();
    }

    // ---- Internal helpers ----

    /// <summary>
    /// Status of every available channel: from the widget's poll data when fresh, otherwise read
    /// from the instrument now (which also refreshes the cache for the next call).
    /// </summary>
    private StatusResult ReadStatus()
    {
        RequireConnection();
        var model = _ctx.Model;
        int count = model.ChannelCount;

        if (_ctx.Cache.TryGetFresh(count, _ctx.Device.WriteCount, DateTime.UtcNow,
                MaxMeasAge, MaxSettingsAge, out var cached, out var measAge, out var setAge))
        {
            return Build(model, cached, "cache", measAge, setAge);
        }

        var fresh = new StatusCache.Sample[count];
        for (int ch = 1; ch <= count; ch++)
        {
            long writesBefore = _ctx.Device.WriteCount;
            var r = _ctx.Device.ReadChannel(ch);
            var now = DateTime.UtcNow;
            _ctx.Cache.UpdateFull(ch, r, writesBefore, now);   // ignored unless every query succeeded
            fresh[ch - 1] = StatusCache.Sample.FromReading(r, writesBefore, now);
        }
        return Build(model, fresh, "device", TimeSpan.Zero, TimeSpan.Zero);
    }

    private StatusResult Build(Dp800Model model, IReadOnlyList<StatusCache.Sample> samples,
        string source, TimeSpan measAge, TimeSpan setAge)
    {
        var list = new List<ChannelStatus>(samples.Count);
        for (int i = 0; i < samples.Count; i++)
        {
            int ch = i + 1;
            var s = samples[i];
            var rating = model.RatingFor(ch);
            list.Add(new ChannelStatus(
                ch, s.OutputOn,
                new MeasuredValues(R(s.MeasVolts), R(s.MeasAmps), R(s.MeasWatts)),
                new SetpointValues(R(s.SetVolts), R(s.SetAmps)),
                s.Mode,
                new ProtectionState(s.OcpOn, R(s.OcpValue), s.OcpOn && s.OcpTripped),
                new ProtectionState(s.OvpOn, R(s.OvpValue), s.OvpOn && s.OvpTripped),
                new LimitValues(
                    R(_ctx.Limits.EffectiveMaxVolts(ch, rating)),
                    R(_ctx.Limits.EffectiveMaxAmps(ch, rating)),
                    _ctx.Limits.HasUserLimit(ch))));
        }
        return new StatusResult(model.Name, _ctx.ControlAllowed, list, source,
            (int)Math.Max(0, measAge.TotalMilliseconds), (int)Math.Max(0, setAge.TotalMilliseconds));
    }

    private void RequireConnection()
    {
        if (!_ctx.IsConnected) throw new McpException("Device is not connected.");
    }

    private void RequireControl()
    {
        if (!_ctx.ControlAllowed)
            throw new McpException("Control is disabled. Enable 'Allow MCP Control' from the widget right-click menu.");
    }

    /// <summary>Validate control-allowed, connection, and channel; returns the channel rating.</summary>
    private ChannelRating Guard(int channel)
    {
        RequireControl();
        RequireConnection();
        if (!_ctx.Model.HasChannel(channel))
            throw new McpException($"Invalid channel: {channel} (this model has {_ctx.Model.ChannelCount} channel(s): CH1..CH{_ctx.Model.ChannelCount}).");
        return _ctx.Model.RatingFor(channel);
    }

    private static McpException SendFailed() => new("Send failed (communication error).");

    private static double Clamp(double v, double lo, double hi) => Math.Round(Math.Clamp(v, lo, hi), 3);
    private static double R(double v) => Math.Round(v, 3);
    private static string F(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);
    private static string Ok(string m) => "OK: " + m;
}

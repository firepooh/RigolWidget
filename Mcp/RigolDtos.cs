using System.ComponentModel;

namespace RigolWidget.Mcp;

// Result types for the read tools. Declared explicitly (instead of anonymous objects) so the SDK
// can publish a real outputSchema and clients can parse structuredContent by schema.

/// <summary>Device identity result of get_identity.</summary>
public sealed record IdentityResult(
    [property: Description("Detected model name (e.g. DP832)")] string Model,
    [property: Description("Raw *IDN? response from the instrument")] string Idn,
    [property: Description("Number of channels this model provides")] int ChannelCount);

/// <summary>Measured values of one channel.</summary>
public sealed record MeasuredValues(
    [property: Description("Measured output voltage (V)")] double Volts,
    [property: Description("Measured output current (A)")] double Amps,
    [property: Description("Measured output power (W)")] double Watts);

/// <summary>Configured setpoints of one channel.</summary>
public sealed record SetpointValues(
    [property: Description("Voltage setpoint (V)")] double Volts,
    [property: Description("Current limit setpoint (A)")] double Amps);

/// <summary>Protection (OCP/OVP) state of one channel.</summary>
public sealed record ProtectionState(
    [property: Description("Whether the protection is enabled")] bool Enabled,
    [property: Description("Protection threshold (A for OCP, V for OVP)")] double Threshold,
    [property: Description("Whether the protection has tripped and needs clearing")] bool Tripped);

/// <summary>Full state of one channel.</summary>
public sealed record ChannelStatus(
    [property: Description("Channel number (1-based)")] int Channel,
    [property: Description("Whether the channel output is on")] bool OutputOn,
    MeasuredValues Measured,
    SetpointValues Setpoint,
    [property: Description("Operating mode reported by the instrument: CV, CC or UR")] string Mode,
    [property: Description("Over-current protection")] ProtectionState Ocp,
    [property: Description("Over-voltage protection")] ProtectionState Ovp);

/// <summary>Result of get_status: device-wide info plus every available channel.</summary>
public sealed record StatusResult(
    [property: Description("Detected model name")] string Model,
    [property: Description("Whether write/control tools are currently allowed by the widget")] bool ControlAllowed,
    [property: Description("State of each channel this model provides")] IReadOnlyList<ChannelStatus> Channels);

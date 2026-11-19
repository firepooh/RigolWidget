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

/// <summary>Setpoint ceilings the widget enforces for one channel.</summary>
public sealed record LimitValues(
    [property: Description("Highest voltage setpoint allowed (V): the user limit if one is set, otherwise the model rating")] double MaxVolts,
    [property: Description("Highest current setpoint allowed (A): the user limit if one is set, otherwise the model rating")] double MaxAmps,
    [property: Description("True when the user set a limit in the widget. Requests above it are refused, and the output cannot be turned on while a setpoint is above it.")] bool UserDefined);

/// <summary>Full state of one channel.</summary>
public sealed record ChannelStatus(
    [property: Description("Channel number (1-based)")] int Channel,
    [property: Description("Whether the channel output is on")] bool OutputOn,
    MeasuredValues Measured,
    SetpointValues Setpoint,
    [property: Description("Operating mode reported by the instrument: CV, CC or UR")] string Mode,
    [property: Description("Over-current protection")] ProtectionState Ocp,
    [property: Description("Over-voltage protection")] ProtectionState Ovp,
    [property: Description("Setpoint limits enforced by the widget")] LimitValues Limits);

/// <summary>Result of get_status: device-wide info plus every available channel.</summary>
public sealed record StatusResult(
    [property: Description("Detected model name")] string Model,
    [property: Description("Whether write/control tools are currently allowed by the widget")] bool ControlAllowed,
    [property: Description("State of each channel this model provides")] IReadOnlyList<ChannelStatus> Channels,
    [property: Description("'cache' = taken from the widget's live polling (no extra instrument queries); 'device' = read from the instrument for this call")] string Source,
    [property: Description("Age of the oldest measured value in milliseconds (0 when read from the device just now)")] int MeasurementAgeMs,
    [property: Description("Age of the oldest setpoint/output/protection value in milliseconds (0 when read from the device just now)")] int SettingsAgeMs);

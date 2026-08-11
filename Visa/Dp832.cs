using System.Globalization;

namespace RigolWidget.Visa;

/// <summary>
/// RIGOL DP832 SCPI control wrapper. Runs on top of RigolConnection and
/// uses only channels 1 (CH1) and 2 (CH2).
/// Queries return success/failure independently per field (if one fails, the rest are still usable).
/// </summary>
public sealed class Dp832
{
    private readonly RigolConnection _conn;

    public Dp832(RigolConnection conn) => _conn = conn;

    public bool IsConnected => _conn.IsConnected;

    private static string Ch(int channel) => "CH" + channel;

    /// <summary>Read the device identity string (*IDN?).</summary>
    public bool TryGetIdentity(out string idn)
        => _conn.Query("*IDN?", out idn) && !string.IsNullOrWhiteSpace(idn);

    // ---- Set (write) ----

    // Upper clamping is done by the caller (based on model ratings); here we only prevent negatives.

    /// <summary>Set output voltage (V).</summary>
    public bool SetVoltage(int channel, double volts)
    {
        volts = Math.Max(0, volts);
        return _conn.Write($":SOUR{channel}:VOLT {volts.ToString("0.000", CultureInfo.InvariantCulture)}");
    }

    /// <summary>Set output current limit (A).</summary>
    public bool SetCurrent(int channel, double amps)
    {
        amps = Math.Max(0, amps);
        return _conn.Write($":SOUR{channel}:CURR {amps.ToString("0.000", CultureInfo.InvariantCulture)}");
    }

    /// <summary>Output ON/OFF.</summary>
    public bool SetOutput(int channel, bool on)
        => _conn.Write($":OUTP {Ch(channel)},{(on ? "ON" : "OFF")}");

    /// <summary>OCP (over-current protection) ON/OFF.</summary>
    public bool SetOcp(int channel, bool on)
        => _conn.Write($":OUTP:OCP {Ch(channel)},{(on ? "ON" : "OFF")}");

    /// <summary>Set OCP threshold current (A).</summary>
    public bool SetOcpValue(int channel, double amps)
    {
        amps = Math.Max(0, amps);
        return _conn.Write($":OUTP:OCP:VAL {Ch(channel)},{amps.ToString("0.000", CultureInfo.InvariantCulture)}");
    }

    /// <summary>OVP (over-voltage protection) ON/OFF.</summary>
    public bool SetOvp(int channel, bool on)
        => _conn.Write($":OUTP:OVP {Ch(channel)},{(on ? "ON" : "OFF")}");

    /// <summary>Set OVP threshold voltage (V).</summary>
    public bool SetOvpValue(int channel, double volts)
    {
        volts = Math.Max(0.01, volts);
        return _conn.Write($":OUTP:OVP:VAL {Ch(channel)},{volts.ToString("0.000", CultureInfo.InvariantCulture)}");
    }

    /// <summary>Clear OCP trip (alarm).</summary>
    public bool ClearOcpAlarm(int channel)
        => _conn.Write($":OUTP:OCP:CLEAR {Ch(channel)}");

    /// <summary>Clear OVP trip (alarm).</summary>
    public bool ClearOvpAlarm(int channel)
        => _conn.Write($":OUTP:OVP:CLEAR {Ch(channel)}");

    // ---- Query (independent per field) ----

    /// <summary>Measured voltage (V).</summary>
    public bool TryGetMeasVoltage(int channel, out double volts)
        => QueryDouble($":MEAS:VOLT? {Ch(channel)}", out volts);

    /// <summary>Measured current (A).</summary>
    public bool TryGetMeasCurrent(int channel, out double amps)
        => QueryDouble($":MEAS:CURR? {Ch(channel)}", out amps);

    /// <summary>Set voltage (V) - the last setpoint stored on the device.</summary>
    public bool TryGetSetVoltage(int channel, out double volts)
        => QueryDouble($":SOUR{channel}:VOLT?", out volts);

    /// <summary>Set current limit (A) - the last setpoint stored on the device.</summary>
    public bool TryGetSetCurrent(int channel, out double amps)
        => QueryDouble($":SOUR{channel}:CURR?", out amps);

    /// <summary>Operating mode: "CV" | "CC" | "UR".</summary>
    public bool TryGetMode(int channel, out string mode)
    {
        mode = "";
        if (!_conn.Query($":OUTP:MODE? {Ch(channel)}", out string resp))
            return false;
        mode = resp.Trim().ToUpperInvariant();
        if (mode is "CV" or "CC" or "UR")
            return true;
        DebugLog.Write($"Parse failed: cmd=':OUTP:MODE? {Ch(channel)}' resp='{resp}'");
        return false;
    }

    /// <summary>Output ON/OFF state.</summary>
    public bool TryGetOutputState(int channel, out bool on)
        => QueryBool($":OUTP? {Ch(channel)}", out on);

    /// <summary>OCP ON/OFF state.</summary>
    public bool TryGetOcpState(int channel, out bool on)
        => QueryBool($":OUTP:OCP? {Ch(channel)}", out on);

    /// <summary>OCP threshold current (A).</summary>
    public bool TryGetOcpValue(int channel, out double amps)
        => QueryDouble($":OUTP:OCP:VAL? {Ch(channel)}", out amps);

    /// <summary>Whether an OCP trip (alarm) has occurred.</summary>
    public bool TryGetOcpAlarm(int channel, out bool tripped)
        => QueryBool($":OUTP:OCP:ALAR? {Ch(channel)}", out tripped);

    /// <summary>OVP ON/OFF state.</summary>
    public bool TryGetOvpState(int channel, out bool on)
        => QueryBool($":OUTP:OVP? {Ch(channel)}", out on);

    /// <summary>OVP threshold voltage (V).</summary>
    public bool TryGetOvpValue(int channel, out double volts)
        => QueryDouble($":OUTP:OVP:VAL? {Ch(channel)}", out volts);

    /// <summary>Whether an OVP trip (alarm) has occurred.</summary>
    public bool TryGetOvpAlarm(int channel, out bool tripped)
        => QueryBool($":OUTP:OVP:ALAR? {Ch(channel)}", out tripped);

    /// <summary>Batched measurement query: read voltage/current/power at once (:MEAS:ALL?).</summary>
    public bool TryReadMeasurementAll(int channel, out double volts, out double amps, out double watts)
    {
        volts = 0; amps = 0; watts = 0;
        if (!_conn.Query($":MEAS:ALL? {Ch(channel)}", out string resp))
            return false;

        // Response: "12.000,1.000,12.000" (V,A,W)
        var parts = resp.Trim().Split(',');
        if (parts.Length >= 3
            && double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out volts)
            && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out amps)
            && double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out watts))
        {
            // Guard against implausible readings. Instruments can return sentinels like
            // 9.9E37 (SCPI infinity) or garbage (e.g. 4294967295) on an unavailable/desynced
            // read; those parse fine but must never be shown. DP800 max is 60V/10A.
            if (Plausible(volts, 100) && Plausible(amps, 20) && Plausible(watts, 500))
                return true;

            DebugLog.Write($"Implausible measurement discarded: cmd=':MEAS:ALL? {Ch(channel)}' resp='{resp}'");
            volts = 0; amps = 0; watts = 0;
            return false;
        }

        DebugLog.Write($"Parse failed: cmd=':MEAS:ALL? {Ch(channel)}' resp='{resp}'");
        return false;
    }

    /// <summary>Within a sane physical range (small negative noise allowed, no huge sentinels).</summary>
    private static bool Plausible(double v, double max) => v >= -1.0 && v <= max;

    /// <summary>Batched setpoint query: read set voltage/current at once (:APPL?).</summary>
    public bool TryGetApplied(int channel, out double setVolts, out double setAmps)
    {
        setVolts = 0; setAmps = 0;
        if (!_conn.Query($":APPL? {Ch(channel)}", out string resp))
            return false;

        // Response: "CH1:30V/3A,12.000,1.0000" (rating, set V, set A)
        var parts = resp.Trim().Split(',');
        if (parts.Length >= 3
            && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out setVolts)
            && double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out setAmps))
        {
            if (Plausible(setVolts, 100) && Plausible(setAmps, 20))
                return true;

            DebugLog.Write($"Implausible setpoint discarded: cmd=':APPL? {Ch(channel)}' resp='{resp}'");
            setVolts = 0; setAmps = 0;
            return false;
        }

        DebugLog.Write($"Parse failed: cmd=':APPL? {Ch(channel)}' resp='{resp}'");
        return false;
    }

    // ---- Internal helpers ----

    private bool QueryDouble(string cmd, out double value)
    {
        value = 0;
        if (!_conn.Query(cmd, out string resp))
            return false;

        if (double.TryParse(resp, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
            return true;

        // Handle firmware that appends a unit, e.g. "12.000V".
        string trimmed = resp.TrimEnd('V', 'A', 'v', 'a', ' ');
        if (double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
            return true;

        DebugLog.Write($"Parse failed: cmd='{cmd}' resp='{resp}'");
        return false;
    }

    private bool QueryBool(string cmd, out bool value)
    {
        value = false;
        if (!_conn.Query(cmd, out string resp))
            return false;

        string r = resp.Trim();
        if (r is "1" || r.StartsWith("ON", StringComparison.OrdinalIgnoreCase)
                     || r.StartsWith("YES", StringComparison.OrdinalIgnoreCase))
        {
            value = true;
            return true;
        }
        if (r is "0" || r.StartsWith("OFF", StringComparison.OrdinalIgnoreCase)
                     || r.StartsWith("NO", StringComparison.OrdinalIgnoreCase))
        {
            value = false;
            return true;
        }

        DebugLog.Write($"Parse failed: cmd='{cmd}' resp='{resp}'");
        return false;
    }
}

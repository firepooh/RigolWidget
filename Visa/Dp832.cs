using System.Globalization;

namespace RigolWidget.Visa;

/// <summary>
/// RIGOL DP832 SCPI 제어 래퍼. RigolConnection 위에서 동작하며
/// 채널은 1(CH1), 2(CH2)만 사용한다.
/// 조회는 필드별로 독립적으로 성공/실패를 반환한다(하나 실패해도 나머지는 사용 가능).
/// </summary>
public sealed class Dp832
{
    private readonly RigolConnection _conn;

    public Dp832(RigolConnection conn) => _conn = conn;

    public bool IsConnected => _conn.IsConnected;

    private static string Ch(int channel) => "CH" + channel;

    // ---- 설정(Write) ----

    /// <summary>출력 전압 설정(V). 범위 0~30V로 클램프.</summary>
    public bool SetVoltage(int channel, double volts)
    {
        volts = Math.Clamp(volts, 0, 30);
        return _conn.Write($":SOUR{channel}:VOLT {volts.ToString("0.000", CultureInfo.InvariantCulture)}");
    }

    /// <summary>출력 전류 리미트 설정(A). 범위 0~3A로 클램프.</summary>
    public bool SetCurrent(int channel, double amps)
    {
        amps = Math.Clamp(amps, 0, 3);
        return _conn.Write($":SOUR{channel}:CURR {amps.ToString("0.000", CultureInfo.InvariantCulture)}");
    }

    /// <summary>출력 ON/OFF.</summary>
    public bool SetOutput(int channel, bool on)
        => _conn.Write($":OUTP {Ch(channel)},{(on ? "ON" : "OFF")}");

    /// <summary>OCP(과전류 보호) ON/OFF.</summary>
    public bool SetOcp(int channel, bool on)
        => _conn.Write($":OUTP:OCP {Ch(channel)},{(on ? "ON" : "OFF")}");

    /// <summary>OCP 전류 임계값(A) 설정. 범위 0~3.3A로 클램프(DP832 스펙).</summary>
    public bool SetOcpValue(int channel, double amps)
    {
        amps = Math.Clamp(amps, 0, 3.3);
        return _conn.Write($":OUTP:OCP:VAL {Ch(channel)},{amps.ToString("0.000", CultureInfo.InvariantCulture)}");
    }

    /// <summary>OVP(과전압 보호) ON/OFF.</summary>
    public bool SetOvp(int channel, bool on)
        => _conn.Write($":OUTP:OVP {Ch(channel)},{(on ? "ON" : "OFF")}");

    /// <summary>OVP 전압 임계값(V) 설정. 범위 0.01~33V로 클램프(DP832 스펙).</summary>
    public bool SetOvpValue(int channel, double volts)
    {
        volts = Math.Clamp(volts, 0.01, 33);
        return _conn.Write($":OUTP:OVP:VAL {Ch(channel)},{volts.ToString("0.000", CultureInfo.InvariantCulture)}");
    }

    /// <summary>OCP 트립(알람) 해제.</summary>
    public bool ClearOcpAlarm(int channel)
        => _conn.Write($":OUTP:OCP:CLEAR {Ch(channel)}");

    /// <summary>OVP 트립(알람) 해제.</summary>
    public bool ClearOvpAlarm(int channel)
        => _conn.Write($":OUTP:OVP:CLEAR {Ch(channel)}");

    // ---- 조회(Query, 필드별 독립) ----

    /// <summary>실측 전압(V).</summary>
    public bool TryGetMeasVoltage(int channel, out double volts)
        => QueryDouble($":MEAS:VOLT? {Ch(channel)}", out volts);

    /// <summary>실측 전류(A).</summary>
    public bool TryGetMeasCurrent(int channel, out double amps)
        => QueryDouble($":MEAS:CURR? {Ch(channel)}", out amps);

    /// <summary>설정 전압(V) — 장비에 저장된 마지막 설정값.</summary>
    public bool TryGetSetVoltage(int channel, out double volts)
        => QueryDouble($":SOUR{channel}:VOLT?", out volts);

    /// <summary>설정 전류 리미트(A) — 장비에 저장된 마지막 설정값.</summary>
    public bool TryGetSetCurrent(int channel, out double amps)
        => QueryDouble($":SOUR{channel}:CURR?", out amps);

    /// <summary>동작 모드: "CV" | "CC" | "UR".</summary>
    public bool TryGetMode(int channel, out string mode)
    {
        mode = "";
        if (!_conn.Query($":OUTP:MODE? {Ch(channel)}", out string resp))
            return false;
        mode = resp.Trim().ToUpperInvariant();
        if (mode is "CV" or "CC" or "UR")
            return true;
        DebugLog.Write($"파싱 실패: cmd=':OUTP:MODE? {Ch(channel)}' resp='{resp}'");
        return false;
    }

    /// <summary>출력 ON/OFF 상태.</summary>
    public bool TryGetOutputState(int channel, out bool on)
        => QueryBool($":OUTP? {Ch(channel)}", out on);

    /// <summary>OCP ON/OFF 상태.</summary>
    public bool TryGetOcpState(int channel, out bool on)
        => QueryBool($":OUTP:OCP? {Ch(channel)}", out on);

    /// <summary>OCP 전류 임계값(A).</summary>
    public bool TryGetOcpValue(int channel, out double amps)
        => QueryDouble($":OUTP:OCP:VAL? {Ch(channel)}", out amps);

    /// <summary>OCP 트립(알람) 발생 여부.</summary>
    public bool TryGetOcpAlarm(int channel, out bool tripped)
        => QueryBool($":OUTP:OCP:ALAR? {Ch(channel)}", out tripped);

    /// <summary>OVP ON/OFF 상태.</summary>
    public bool TryGetOvpState(int channel, out bool on)
        => QueryBool($":OUTP:OVP? {Ch(channel)}", out on);

    /// <summary>OVP 전압 임계값(V).</summary>
    public bool TryGetOvpValue(int channel, out double volts)
        => QueryDouble($":OUTP:OVP:VAL? {Ch(channel)}", out volts);

    /// <summary>OVP 트립(알람) 발생 여부.</summary>
    public bool TryGetOvpAlarm(int channel, out bool tripped)
        => QueryBool($":OUTP:OVP:ALAR? {Ch(channel)}", out tripped);

    /// <summary>측정값 묶음 질의: 전압/전류/전력을 한 번에 읽는다 (:MEAS:ALL?).</summary>
    public bool TryReadMeasurementAll(int channel, out double volts, out double amps, out double watts)
    {
        volts = 0; amps = 0; watts = 0;
        if (!_conn.Query($":MEAS:ALL? {Ch(channel)}", out string resp))
            return false;

        // 응답: "12.000,1.000,12.000" (V,A,W)
        var parts = resp.Trim().Split(',');
        if (parts.Length >= 3
            && double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out volts)
            && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out amps)
            && double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out watts))
            return true;

        DebugLog.Write($"파싱 실패: cmd=':MEAS:ALL? {Ch(channel)}' resp='{resp}'");
        return false;
    }

    /// <summary>설정값 묶음 질의: 설정 전압/전류를 한 번에 읽는다 (:APPL?).</summary>
    public bool TryGetApplied(int channel, out double setVolts, out double setAmps)
    {
        setVolts = 0; setAmps = 0;
        if (!_conn.Query($":APPL? {Ch(channel)}", out string resp))
            return false;

        // 응답: "CH1:30V/3A,12.000,1.0000" (정격, 설정V, 설정A)
        var parts = resp.Trim().Split(',');
        if (parts.Length >= 3
            && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out setVolts)
            && double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out setAmps))
            return true;

        DebugLog.Write($"파싱 실패: cmd=':APPL? {Ch(channel)}' resp='{resp}'");
        return false;
    }

    // ---- 내부 헬퍼 ----

    private bool QueryDouble(string cmd, out double value)
    {
        value = 0;
        if (!_conn.Query(cmd, out string resp))
            return false;

        if (double.TryParse(resp, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
            return true;

        // 일부 펌웨어가 "12.000V"처럼 단위를 붙이는 경우 대비.
        string trimmed = resp.TrimEnd('V', 'A', 'v', 'a', ' ');
        if (double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
            return true;

        DebugLog.Write($"파싱 실패: cmd='{cmd}' resp='{resp}'");
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

        DebugLog.Write($"파싱 실패: cmd='{cmd}' resp='{resp}'");
        return false;
    }
}

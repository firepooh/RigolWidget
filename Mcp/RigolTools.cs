using System.ComponentModel;
using System.Globalization;
using ModelContextProtocol.Server;
using RigolWidget.Visa;

namespace RigolWidget.Mcp;

/// <summary>
/// RIGOL DP800 전원공급기를 제어/조회하는 MCP 도구 모음.
/// 쓰기 도구는 컨텍스트의 ControlAllowed가 켜져 있어야 동작한다(기본 읽기 전용).
/// 모든 설정값은 감지된 모델 정격으로 클램프된다.
/// </summary>
[McpServerToolType]
public sealed class RigolTools
{
    private readonly RigolMcpContext _ctx;

    public RigolTools(RigolMcpContext ctx) => _ctx = ctx;

    // ---- 조회 ----

    [McpServerTool(Name = "get_identity")]
    [Description("장비 식별 정보(*IDN?)와 감지된 모델명을 반환한다.")]
    public string GetIdentity()
    {
        if (!_ctx.IsConnected) return Err("장비가 연결되어 있지 않습니다.");
        _ctx.Device.TryGetIdentity(out string idn);
        return $"{{\"model\":\"{_ctx.Model.Name}\",\"idn\":\"{idn.Trim()}\"}}";
    }

    [McpServerTool(Name = "get_status")]
    [Description("모든 채널의 측정값(전압/전류), 설정값, 출력 상태, CV/CC 모드, OCP/OVP 보호 상태와 트립 여부를 반환한다.")]
    public object GetStatus()
    {
        if (!_ctx.IsConnected) return new { error = "장비가 연결되어 있지 않습니다." };

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

    // ---- 제어(쓰기) ----

    [McpServerTool(Name = "set_voltage")]
    [Description("지정 채널의 출력 전압(V)을 설정한다. 값은 모델 정격으로 클램프된다.")]
    public string SetVoltage(
        [Description("채널 번호 (1 또는 2)")] int channel,
        [Description("설정 전압(V)")] double volts)
    {
        if (!TryGuard(channel, out string err, out var rating)) return err;
        double v = Clamp(volts, 0, rating.VMax);
        bool ok = _ctx.Device.SetVoltage(channel, v);
        _ctx.NotifyCommand($"[MCP] CH{channel} 전압 설정 {F(v)}V");
        return ok ? Ok($"CH{channel} 전압을 {F(v)}V로 설정했습니다.") : Err("전송 실패(통신 오류).");
    }

    [McpServerTool(Name = "set_current")]
    [Description("지정 채널의 출력 전류 리미트(A)를 설정한다. 값은 모델 정격으로 클램프된다.")]
    public string SetCurrent(
        [Description("채널 번호 (1 또는 2)")] int channel,
        [Description("설정 전류(A)")] double amps)
    {
        if (!TryGuard(channel, out string err, out var rating)) return err;
        double a = Clamp(amps, 0, rating.IMax);
        bool ok = _ctx.Device.SetCurrent(channel, a);
        _ctx.NotifyCommand($"[MCP] CH{channel} 전류 설정 {F(a)}A");
        return ok ? Ok($"CH{channel} 전류 리미트를 {F(a)}A로 설정했습니다.") : Err("전송 실패(통신 오류).");
    }

    [McpServerTool(Name = "set_output")]
    [Description("지정 채널의 출력을 켜거나 끈다.")]
    public string SetOutput(
        [Description("채널 번호 (1 또는 2)")] int channel,
        [Description("true=출력 ON, false=출력 OFF")] bool on)
    {
        if (!TryGuard(channel, out string err, out _)) return err;
        bool ok = _ctx.Device.SetOutput(channel, on);
        _ctx.NotifyCommand($"[MCP] CH{channel} 출력 {(on ? "ON" : "OFF")}");
        return ok ? Ok($"CH{channel} 출력을 {(on ? "켰습니다" : "껐습니다")}.") : Err("전송 실패(통신 오류).");
    }

    [McpServerTool(Name = "set_ocp")]
    [Description("지정 채널의 과전류 보호(OCP)를 켜거나 끄고, 선택적으로 임계 전류(A)를 설정한다.")]
    public string SetOcp(
        [Description("채널 번호 (1 또는 2)")] int channel,
        [Description("true=OCP 활성, false=비활성")] bool enabled,
        [Description("임계 전류(A). 생략하면 현재값 유지")] double? amps = null)
    {
        if (!TryGuard(channel, out string err, out var rating)) return err;
        string extra = "";
        if (amps is double a)
        {
            double c = Clamp(a, 0, rating.OcpMax);
            _ctx.Device.SetOcpValue(channel, c);
            extra = $", 임계 {F(c)}A";
        }
        bool ok = _ctx.Device.SetOcp(channel, enabled);
        _ctx.NotifyCommand($"[MCP] CH{channel} OCP {(enabled ? "ON" : "OFF")}{extra}");
        return ok ? Ok($"CH{channel} OCP {(enabled ? "활성" : "비활성")}{extra} 완료.") : Err("전송 실패(통신 오류).");
    }

    [McpServerTool(Name = "set_ovp")]
    [Description("지정 채널의 과전압 보호(OVP)를 켜거나 끄고, 선택적으로 임계 전압(V)을 설정한다.")]
    public string SetOvp(
        [Description("채널 번호 (1 또는 2)")] int channel,
        [Description("true=OVP 활성, false=비활성")] bool enabled,
        [Description("임계 전압(V). 생략하면 현재값 유지")] double? volts = null)
    {
        if (!TryGuard(channel, out string err, out var rating)) return err;
        string extra = "";
        if (volts is double v)
        {
            double c = Clamp(v, 0.01, rating.OvpMax);
            _ctx.Device.SetOvpValue(channel, c);
            extra = $", 임계 {F(c)}V";
        }
        bool ok = _ctx.Device.SetOvp(channel, enabled);
        _ctx.NotifyCommand($"[MCP] CH{channel} OVP {(enabled ? "ON" : "OFF")}{extra}");
        return ok ? Ok($"CH{channel} OVP {(enabled ? "활성" : "비활성")}{extra} 완료.") : Err("전송 실패(통신 오류).");
    }

    [McpServerTool(Name = "clear_trip")]
    [Description("지정 채널의 보호 트립(알람)을 해제한다. kind는 'ocp' 또는 'ovp'.")]
    public string ClearTrip(
        [Description("채널 번호 (1 또는 2)")] int channel,
        [Description("'ocp'(과전류) 또는 'ovp'(과전압)")] string kind)
    {
        if (!TryGuard(channel, out string err, out _)) return err;
        kind = kind?.Trim().ToLowerInvariant() ?? "";
        bool ok;
        if (kind == "ocp") ok = _ctx.Device.ClearOcpAlarm(channel);
        else if (kind == "ovp") ok = _ctx.Device.ClearOvpAlarm(channel);
        else return Err("kind는 'ocp' 또는 'ovp'여야 합니다.");
        _ctx.NotifyCommand($"[MCP] CH{channel} {kind.ToUpperInvariant()} 트립 해제");
        return ok ? Ok($"CH{channel} {kind.ToUpperInvariant()} 트립을 해제했습니다.") : Err("전송 실패(통신 오류).");
    }

    // ---- 내부 헬퍼 ----

    /// <summary>제어 허용·연결·채널 유효성 검사. 실패 시 err에 사유.</summary>
    private bool TryGuard(int channel, out string err, out ChannelRating rating)
    {
        rating = _ctx.Model.RatingFor(channel);
        err = "";
        if (!_ctx.ControlAllowed)
        {
            err = Err("제어가 비활성화되어 있습니다. 위젯 우클릭 메뉴에서 'MCP 제어 허용'을 켜세요.");
            return false;
        }
        if (!_ctx.IsConnected)
        {
            err = Err("장비가 연결되어 있지 않습니다.");
            return false;
        }
        if (channel < 1 || channel > 2 || (channel == 2 && !_ctx.Model.HasCh2))
        {
            err = Err($"유효하지 않은 채널: {channel} (이 모델은 {(_ctx.Model.HasCh2 ? "CH1·CH2" : "CH1")}만 지원).");
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

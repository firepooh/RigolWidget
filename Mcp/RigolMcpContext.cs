using RigolWidget.Visa;

namespace RigolWidget.Mcp;

/// <summary>
/// MCP 도구가 공유하는 컨텍스트. 실행 중인 위젯과 <b>같은</b> VISA 세션(Dp832)을 사용해
/// 세션 충돌 없이 장비를 제어한다. 스레드 안전성은 RigolConnection의 락이 보장한다.
/// </summary>
public sealed class RigolMcpContext
{
    private readonly Dp832 _device;

    public RigolMcpContext(Dp832 device) => _device = device;

    public Dp832 Device => _device;

    /// <summary>제어(쓰기) 허용 여부. 꺼져 있으면 도구는 읽기만 가능.</summary>
    public volatile bool ControlAllowed;

    /// <summary>현재 감지된 모델(정격 클램프에 사용). 기본 DP832.</summary>
    public volatile Dp800Model Model = Dp800Models.Default;

    /// <summary>MCP발 쓰기 명령 발생 시 호출(로그·UI 즉시 동기화용).</summary>
    public Action<string>? OnCommand;

    public bool IsConnected => _device.IsConnected;

    public void NotifyCommand(string message) => OnCommand?.Invoke(message);
}

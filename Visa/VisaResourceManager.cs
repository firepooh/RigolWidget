using System.Text;

namespace RigolWidget.Visa;

/// <summary>
/// VISA Default Resource Manager 래퍼. USB 장비 탐색 및 세션 오픈을 담당한다.
/// 프로세스당 한 번만 열어 재사용한다.
/// </summary>
public sealed class VisaResourceManager : IDisposable
{
    private int _rm;
    private bool _disposed;

    public VisaResourceManager()
    {
        int status = VisaInterop.viOpenDefaultRM(out _rm);
        if (!VisaInterop.Success(status))
            throw new VisaException("VISA 리소스 매니저를 열 수 없습니다. VISA 런타임(NI-VISA 등) 설치를 확인하세요.", status);
    }

    /// <summary>USB 계측기 리소스 문자열 목록을 반환한다. (예: USB0::0x1AB1::0x0E11::DP8...::INSTR)</summary>
    public IReadOnlyList<string> FindUsbInstruments()
    {
        var results = new List<string>();
        var desc = new StringBuilder(512);

        int status = VisaInterop.viFindRsrc(_rm, "USB?*INSTR", out int findList, out int count, desc);
        if (!VisaInterop.Success(status) || count == 0)
            return results;

        try
        {
            results.Add(desc.ToString());
            for (int i = 1; i < count; i++)
            {
                desc.Clear();
                desc.EnsureCapacity(512);
                if (!VisaInterop.Success(VisaInterop.viFindNext(findList, desc)))
                    break;
                results.Add(desc.ToString());
            }
        }
        finally
        {
            VisaInterop.viClose(findList);
        }
        return results;
    }

    /// <summary>지정한 리소스로 장비 세션을 연다. 실패 시 VisaException.</summary>
    public VisaSession Open(string resource, int timeoutMs = 1500)
    {
        int status = VisaInterop.viOpen(_rm, resource, VisaInterop.VI_NULL, 0, out int vi);
        if (!VisaInterop.Success(status))
            throw new VisaException($"장비를 열 수 없습니다: {resource}", status);

        // I/O 타임아웃 설정 (실패해도 기본값으로 진행).
        VisaInterop.viSetAttribute(vi, VisaInterop.VI_ATTR_TMO_VALUE, timeoutMs);
        return new VisaSession(vi, resource);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_rm != 0) VisaInterop.viClose(_rm);
        _rm = 0;
    }
}

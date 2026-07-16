using System.Runtime.InteropServices;
using System.Text;

namespace RigolWidget.Visa;

/// <summary>
/// visa32.dll(VISA C API) 직접 P/Invoke 바인딩.
/// 별도 NuGet/COM 의존성 없이 시스템에 설치된 VISA 런타임(NI-VISA 등)을 사용한다.
/// x64 프로세스에서는 System32\visa32.dll(64bit 구현)이 로드된다.
/// </summary>
internal static class VisaInterop
{
    private const string Dll = "visa32.dll";

    // ViStatus 성공 판정: VI_SUCCESS(0) 및 양수 경고는 성공, 음수는 오류.
    public const int VI_SUCCESS = 0;
    public const int VI_NULL = 0;

    // Attributes
    public const int VI_ATTR_TMO_VALUE = 0x3FFF001A;  // 밀리초 단위 I/O 타임아웃

    [DllImport(Dll)]
    public static extern int viOpenDefaultRM(out int sesn);

    [DllImport(Dll, CharSet = CharSet.Ansi)]
    public static extern int viFindRsrc(int sesn, string expr, out int findList, out int retcnt, StringBuilder desc);

    [DllImport(Dll, CharSet = CharSet.Ansi)]
    public static extern int viFindNext(int findList, StringBuilder desc);

    [DllImport(Dll, CharSet = CharSet.Ansi)]
    public static extern int viOpen(int sesn, string name, int accessMode, int openTimeout, out int vi);

    [DllImport(Dll)]
    public static extern int viClose(int vi);

    [DllImport(Dll)]
    public static extern int viSetAttribute(int vi, int attrName, int attrValue);

    [DllImport(Dll)]
    public static extern int viWrite(int vi, byte[] buf, uint count, out uint retCount);

    [DllImport(Dll)]
    public static extern int viRead(int vi, byte[] buf, uint count, out uint retCount);

    [DllImport(Dll)]
    public static extern int viClear(int vi);

    public static bool Success(int status) => status >= 0;
}

/// <summary>VISA 호출 실패 시 던지는 예외.</summary>
public sealed class VisaException : Exception
{
    public int Status { get; }
    public VisaException(string message, int status) : base($"{message} (VISA status 0x{status:X8})")
        => Status = status;
}

using System.Runtime.InteropServices;
using System.Text;

namespace RigolWidget.Visa;

/// <summary>
/// Direct P/Invoke bindings to visa32.dll (the VISA C API).
/// Uses the VISA runtime installed on the system (e.g. NI-VISA) without any extra NuGet/COM dependency.
/// In an x64 process, System32\visa32.dll (the 64-bit implementation) is loaded.
/// </summary>
internal static class VisaInterop
{
    private const string Dll = "visa32.dll";

    // ViStatus success test: VI_SUCCESS(0) and positive warnings are success, negatives are errors.
    public const int VI_SUCCESS = 0;
    public const int VI_NULL = 0;

    // Attributes
    public const int VI_ATTR_TMO_VALUE = 0x3FFF001A;  // I/O timeout in milliseconds

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

/// <summary>Exception thrown when a VISA call fails.</summary>
public sealed class VisaException : Exception
{
    public int Status { get; }
    public VisaException(string message, int status) : base($"{message} (VISA status 0x{status:X8})")
        => Status = status;
}

using System.Text;

namespace RigolWidget.Visa;

/// <summary>
/// Wrapper around the VISA Default Resource Manager. Handles USB device discovery and session opening.
/// Opened once per process and reused.
/// </summary>
public sealed class VisaResourceManager : IDisposable
{
    private int _rm;
    private bool _disposed;

    public VisaResourceManager()
    {
        int status = VisaInterop.viOpenDefaultRM(out _rm);
        if (!VisaInterop.Success(status))
            throw new VisaException("Cannot open the VISA resource manager. Please check that a VISA runtime (e.g. NI-VISA) is installed.", status);
    }

    /// <summary>Returns a list of USB instrument resource strings. (e.g. USB0::0x1AB1::0x0E11::DP8...::INSTR)</summary>
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

    /// <summary>Opens a device session for the given resource. Throws VisaException on failure.</summary>
    public VisaSession Open(string resource, int timeoutMs = 1500)
    {
        int status = VisaInterop.viOpen(_rm, resource, VisaInterop.VI_NULL, 0, out int vi);
        if (!VisaInterop.Success(status))
            throw new VisaException($"Cannot open device: {resource}", status);

        // Set the I/O timeout (proceed with the default if this fails).
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

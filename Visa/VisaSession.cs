using System.Text;

namespace RigolWidget.Visa;

/// <summary>
/// A single open VISA device session. Handles sending SCPI commands and receiving responses.
/// Not thread-safe, so the caller (RigolConnection) serializes access.
/// </summary>
public sealed class VisaSession : IDisposable
{
    private int _vi;
    private bool _disposed;

    public string Resource { get; }

    internal VisaSession(int vi, string resource)
    {
        _vi = vi;
        Resource = resource;
    }

    /// <summary>Send a command (no response). Throws VisaException on failure.</summary>
    public void Write(string command)
    {
        ThrowIfDisposed();
        byte[] buf = Encoding.ASCII.GetBytes(command + "\n");
        int status = VisaInterop.viWrite(_vi, buf, (uint)buf.Length, out _);
        if (!VisaInterop.Success(status))
            throw new VisaException($"Write failed: {command}", status);
    }

    /// <summary>Read the response as a string.</summary>
    public string ReadString()
    {
        ThrowIfDisposed();
        var buf = new byte[4096];
        int status = VisaInterop.viRead(_vi, buf, (uint)buf.Length, out uint read);
        if (!VisaInterop.Success(status))
            throw new VisaException("Read failed", status);
        return Encoding.ASCII.GetString(buf, 0, (int)read).Trim('\r', '\n', ' ', '\0');
    }

    /// <summary>Send a command, then read and return the response.</summary>
    public string Query(string command)
    {
        Write(command);
        return ReadString();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_vi != 0) VisaInterop.viClose(_vi);
        _vi = 0;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(VisaSession));
    }
}

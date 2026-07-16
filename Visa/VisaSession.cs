using System.Text;

namespace RigolWidget.Visa;

/// <summary>
/// 열린 VISA 장비 세션 하나. SCPI 명령 전송/응답을 담당한다.
/// 스레드 안전하지 않으므로 상위(RigolConnection)에서 직렬화한다.
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

    /// <summary>명령 전송(응답 없음). 실패 시 VisaException.</summary>
    public void Write(string command)
    {
        ThrowIfDisposed();
        byte[] buf = Encoding.ASCII.GetBytes(command + "\n");
        int status = VisaInterop.viWrite(_vi, buf, (uint)buf.Length, out _);
        if (!VisaInterop.Success(status))
            throw new VisaException($"쓰기 실패: {command}", status);
    }

    /// <summary>응답을 문자열로 읽는다.</summary>
    public string ReadString()
    {
        ThrowIfDisposed();
        var buf = new byte[4096];
        int status = VisaInterop.viRead(_vi, buf, (uint)buf.Length, out uint read);
        if (!VisaInterop.Success(status))
            throw new VisaException("읽기 실패", status);
        return Encoding.ASCII.GetString(buf, 0, (int)read).Trim('\r', '\n', ' ', '\0');
    }

    /// <summary>명령 전송 후 응답을 읽어 반환한다.</summary>
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

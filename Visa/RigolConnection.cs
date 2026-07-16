namespace RigolWidget.Visa;

/// <summary>
/// 마지막으로 접속한 장비 리소스를 기억하고, 런타임 중 USB 연결이 끊어지면
/// 백그라운드에서 주기적으로 재접속을 시도해 자동 복구한다.
/// 모든 SCPI I/O는 내부 lock으로 직렬화된다.
/// </summary>
public sealed class RigolConnection : IDisposable
{
    private readonly VisaResourceManager _rm;
    private readonly object _ioLock = new();
    private readonly Thread _reconnectThread;
    private readonly CancellationTokenSource _cts = new();
    private VisaSession? _session;
    private volatile bool _connected;
    private bool _disposed;

    public string Resource { get; }

    /// <summary>연결 상태가 바뀔 때 발생(true=연결됨).</summary>
    public event Action<bool>? ConnectionChanged;

    public bool IsConnected => _connected;

    public RigolConnection(VisaResourceManager rm, string resource)
    {
        _rm = rm;
        Resource = resource;

        // 최초 1회 즉시 접속 시도.
        TryOpen();

        _reconnectThread = new Thread(ReconnectLoop)
        {
            IsBackground = true,
            Name = "RigolReconnect"
        };
        _reconnectThread.Start();
    }

    private void ReconnectLoop()
    {
        var token = _cts.Token;
        while (!token.IsCancellationRequested)
        {
            if (!_connected)
                TryOpen();

            // 1초 간격으로 재시도.
            token.WaitHandle.WaitOne(1000);
        }
    }

    private void TryOpen()
    {
        lock (_ioLock)
        {
            if (_connected) return;
            try
            {
                _session?.Dispose();
                _session = _rm.Open(Resource);
                // 접속 검증(*IDN?). 실패하면 예외.
                _ = _session.Query("*IDN?");
                SetConnected(true);
            }
            catch
            {
                _session?.Dispose();
                _session = null;
                SetConnected(false);
            }
        }
    }

    private void Drop()
    {
        // 이미 lock 안에서 호출됨.
        _session?.Dispose();
        _session = null;
        SetConnected(false);
    }

    private void SetConnected(bool value)
    {
        if (_connected == value) return;
        _connected = value;
        DebugLog.Write(value ? $"연결됨: {Resource}" : $"연결 끊김: {Resource} (자동 재접속 시작)");
        ConnectionChanged?.Invoke(value);
    }

    /// <summary>명령 전송. 성공 시 true. 실패 시 연결을 끊고 false(재접속 루프가 복구).</summary>
    public bool Write(string command)
    {
        lock (_ioLock)
        {
            if (_session == null || !_connected) return false;
            try
            {
                _session.Write(command);
                return true;
            }
            catch (Exception ex)
            {
                DebugLog.Write($"쓰기 실패: '{command}' — {ex.Message}");
                Drop();
                return false;
            }
        }
    }

    /// <summary>질의(명령+응답). 성공 시 true.</summary>
    public bool Query(string command, out string response)
    {
        lock (_ioLock)
        {
            response = string.Empty;
            if (_session == null || !_connected) return false;
            try
            {
                response = _session.Query(command);
                return true;
            }
            catch (Exception ex)
            {
                DebugLog.Write($"질의 실패: '{command}' — {ex.Message}");
                Drop();
                return false;
            }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _cts.Cancel();
        try { _reconnectThread.Join(1500); } catch { /* ignore */ }
        lock (_ioLock)
        {
            _session?.Dispose();
            _session = null;
        }
        _cts.Dispose();
    }
}

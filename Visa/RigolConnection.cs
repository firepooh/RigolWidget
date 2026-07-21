namespace RigolWidget.Visa;

/// <summary>
/// Remembers the last connected device resource and, if the USB connection drops at runtime,
/// periodically attempts to reconnect in the background to recover automatically.
/// All SCPI I/O is serialized by an internal lock.
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

    /// <summary>Raised when the connection state changes (true = connected).</summary>
    public event Action<bool>? ConnectionChanged;

    public bool IsConnected => _connected;

    public RigolConnection(VisaResourceManager rm, string resource)
    {
        _rm = rm;
        Resource = resource;

        // Attempt to connect immediately once at startup.
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

            // Retry at 1-second intervals.
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
                // Verify the connection (*IDN?). Throws on failure.
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
        // Already called within the lock.
        _session?.Dispose();
        _session = null;
        SetConnected(false);
    }

    private void SetConnected(bool value)
    {
        if (_connected == value) return;
        _connected = value;
        DebugLog.Write(value ? $"Connected: {Resource}" : $"Disconnected: {Resource} (starting auto-reconnect)");
        ConnectionChanged?.Invoke(value);
    }

    /// <summary>Send a command. Returns true on success. On failure, drops the connection and returns false (the reconnect loop recovers).</summary>
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
                DebugLog.Write($"Write failed: '{command}' — {ex.Message}");
                Drop();
                return false;
            }
        }
    }

    /// <summary>Query (command + response). Returns true on success.</summary>
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
                DebugLog.Write($"Query failed: '{command}' — {ex.Message}");
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

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RigolWidget.Visa;

namespace RigolWidget.Mcp;

/// <summary>
/// 위젯 프로세스에 내장되는 MCP 서버(Streamable HTTP, 127.0.0.1 전용).
/// 실행 중인 위젯과 같은 Dp832 세션을 공유하므로 VISA 세션 충돌이 없다.
/// </summary>
public sealed class RigolMcpServer
{
    private readonly RigolMcpContext _context;
    private WebApplication? _app;

    public RigolMcpServer(RigolMcpContext context) => _context = context;

    public bool IsRunning => _app != null;
    public int Port { get; private set; }
    public string Url => $"http://127.0.0.1:{Port}/";

    /// <summary>서버 시작. 성공하면 true, 실패 시 error에 사유(포트 충돌 등).</summary>
    public async Task<(bool ok, string? error)> StartAsync(int port)
    {
        if (_app != null) return (true, null);
        try
        {
            var builder = WebApplication.CreateSlimBuilder();
            builder.Logging.ClearProviders();                       // WinExe: 콘솔 로깅 불필요
            builder.WebHost.UseUrls($"http://127.0.0.1:{port}");    // 로컬 전용 바인딩

            builder.Services.AddSingleton(_context);
            builder.Services.AddMcpServer()
                .WithHttpTransport()
                .WithTools<RigolTools>();

            var app = builder.Build();
            app.MapMcp();

            await app.StartAsync();
            _app = app;
            Port = port;
            DebugLog.Write($"MCP 서버 시작: {Url} (제어허용={_context.ControlAllowed})");
            return (true, null);
        }
        catch (Exception ex)
        {
            DebugLog.Write($"MCP 서버 시작 실패(port {port}): {ex.Message}");
            return (false, ex.Message);
        }
    }

    public async Task StopAsync()
    {
        if (_app == null) return;
        try
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
            DebugLog.Write("MCP 서버 중지");
        }
        catch (Exception ex)
        {
            DebugLog.Write($"MCP 서버 중지 오류: {ex.Message}");
        }
        finally
        {
            _app = null;
        }
    }
}

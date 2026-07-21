using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RigolWidget.Visa;

namespace RigolWidget.Mcp;

/// <summary>
/// MCP server embedded in the widget process (Streamable HTTP, 127.0.0.1 only).
/// Shares the same Dp832 session as the running widget, so there are no VISA session conflicts.
/// </summary>
public sealed class RigolMcpServer
{
    private readonly RigolMcpContext _context;
    private WebApplication? _app;

    public RigolMcpServer(RigolMcpContext context) => _context = context;

    public bool IsRunning => _app != null;
    public int Port { get; private set; }
    public string Url => $"http://127.0.0.1:{Port}/";

    /// <summary>Start the server. Returns true on success; on failure, error holds the reason (e.g. port conflict).</summary>
    public async Task<(bool ok, string? error)> StartAsync(int port)
    {
        if (_app != null) return (true, null);
        try
        {
            var builder = WebApplication.CreateSlimBuilder();
            builder.Logging.ClearProviders();                       // WinExe: no console logging needed
            builder.WebHost.UseUrls($"http://127.0.0.1:{port}");    // local-only binding

            builder.Services.AddSingleton(_context);
            builder.Services.AddMcpServer()
                .WithHttpTransport()
                .WithTools<RigolTools>();

            var app = builder.Build();
            app.MapMcp();

            await app.StartAsync();
            _app = app;
            Port = port;
            DebugLog.Write($"MCP server started: {Url} (control allowed={_context.ControlAllowed})");
            return (true, null);
        }
        catch (Exception ex)
        {
            DebugLog.Write($"MCP server failed to start (port {port}): {ex.Message}");
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
            DebugLog.Write("MCP server stopped");
        }
        catch (Exception ex)
        {
            DebugLog.Write($"MCP server stop error: {ex.Message}");
        }
        finally
        {
            _app = null;
        }
    }
}

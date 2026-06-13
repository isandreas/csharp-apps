using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using Microsoft.Extensions.Logging;

namespace WebSocketService.Handlers;

/// <summary>
/// Manages WebSocket connections, handles echo and broadcast protocols,
/// and performs graceful drain on shutdown.
/// </summary>
public sealed partial class WebSocketHandler : IAsyncDisposable
{
    private readonly ConcurrentDictionary<Guid, WebSocket> _connections = new();
    private readonly ILogger<WebSocketHandler> _logger;

    public WebSocketHandler(ILogger<WebSocketHandler> logger)
    {
        _logger = logger;
    }

    // ── High-performance LoggerMessage delegates ──────────────────────────────

    [LoggerMessage(Level = LogLevel.Information, Message = "WebSocket connected: {ConnectionId}. Total: {Count}")]
    private partial void LogConnected(Guid connectionId, int count);

    [LoggerMessage(Level = LogLevel.Information, Message = "WebSocket disconnected: {ConnectionId}. Total: {Count}")]
    private partial void LogDisconnected(Guid connectionId, int count);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Received [{ConnectionId}]: {Message}")]
    private partial void LogReceived(Guid connectionId, string message);

    [LoggerMessage(Level = LogLevel.Information, Message = "Broadcasting message from {ConnectionId} to {Count} clients")]
    private partial void LogBroadcast(Guid connectionId, int count);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Error closing WebSocket during drain")]
    private partial void LogDrainError(Exception ex);

    [LoggerMessage(Level = LogLevel.Information, Message = "Draining {Count} WebSocket connections...")]
    private partial void LogDrainStart(int count);

    [LoggerMessage(Level = LogLevel.Information, Message = "WebSocket drain complete.")]
    private partial void LogDrainComplete();

    // ── Public API ─────────────────────────────────────────────────────────────

    /// <summary>Registers the socket and drives the message loop until the connection closes.</summary>
    public async Task HandleAsync(Guid connectionId, WebSocket webSocket, CancellationToken appStopping)
    {
        _connections[connectionId] = webSocket;
        LogConnected(connectionId, _connections.Count);

        try
        {
            await ReceiveLoopAsync(connectionId, webSocket, appStopping);
        }
        finally
        {
            _connections.TryRemove(connectionId, out _);
            LogDisconnected(connectionId, _connections.Count);
        }
    }

    private async Task ReceiveLoopAsync(Guid connectionId, WebSocket webSocket, CancellationToken ct)
    {
        var buffer = new byte[4096];

        while (webSocket.State == WebSocketState.Open && !ct.IsCancellationRequested)
        {
            WebSocketReceiveResult result;
            using var ms = new System.IO.MemoryStream();

            do
            {
                result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    await webSocket.CloseAsync(
                        WebSocketCloseStatus.NormalClosure,
                        "Client requested close",
                        CancellationToken.None);
                    return;
                }

                ms.Write(buffer, 0, result.Count);
            }
            while (!result.EndOfMessage);

            var message = Encoding.UTF8.GetString(ms.ToArray());
            LogReceived(connectionId, message);

            if (message.StartsWith("broadcast:", StringComparison.OrdinalIgnoreCase))
            {
                var body = message["broadcast:".Length..].TrimStart();
                await BroadcastAsync(body, connectionId, ct);
            }
            else
            {
                await SendAsync(webSocket, $"echo: {message}", ct);
            }
        }
    }

    private async Task BroadcastAsync(string body, Guid senderConnectionId, CancellationToken ct)
    {
        LogBroadcast(senderConnectionId, _connections.Count);

        var tasks = _connections.Values
            .Where(ws => ws.State == WebSocketState.Open)
            .Select(ws => SendAsync(ws, body, ct));

        await Task.WhenAll(tasks);
    }

    private static async Task SendAsync(WebSocket webSocket, string message, CancellationToken ct)
    {
        var encoded = Encoding.UTF8.GetBytes(message);
        await webSocket.SendAsync(
            new ArraySegment<byte>(encoded),
            WebSocketMessageType.Text,
            endOfMessage: true,
            cancellationToken: ct);
    }

    /// <summary>Drains all open connections with a close frame on application shutdown.</summary>
    public async Task DrainAsync()
    {
        LogDrainStart(_connections.Count);

        var closeTasks = _connections.Values
            .Where(ws => ws.State == WebSocketState.Open)
            .Select(async ws =>
            {
                try
                {
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                    await ws.CloseAsync(
                        WebSocketCloseStatus.EndpointUnavailable,
                        "Server shutting down",
                        cts.Token);
                }
                catch (Exception ex)
                {
                    LogDrainError(ex);
                }
            });

        await Task.WhenAll(closeTasks);
        LogDrainComplete();
    }

    public async ValueTask DisposeAsync()
    {
        await DrainAsync();
    }
}

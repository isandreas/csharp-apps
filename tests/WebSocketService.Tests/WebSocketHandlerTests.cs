using System.Net;
using System.Net.WebSockets;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Xunit;

namespace WebSocketService.Tests;

/// <summary>
/// WebApplicationFactory for the WebSocket service with a test API key.
/// </summary>
public sealed class WsWebApplicationFactory : WebApplicationFactory<Program>
{
    public const string TestApiKey = "test-ws-key-99999";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("WS_API_KEY", TestApiKey);
    }
}

public sealed class WebSocketHandlerTests : IClassFixture<WsWebApplicationFactory>
{
    private readonly WsWebApplicationFactory _factory;

    public WebSocketHandlerTests(WsWebApplicationFactory factory)
    {
        _factory = factory;
    }

    // ── /healthz ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task HealthCheck_Returns200_WithoutApiKey()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/healthz");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ── WebSocket handshake auth ───────────────────────────────────────────────

    [Fact]
    public async Task WsHandshake_WithoutApiKey_Returns401()
    {
        var client = _factory.CreateClient();
        // Plain HTTP GET to /ws without upgrade — middleware should reject it
        var response = await client.GetAsync("/ws");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task WsHandshake_WithWrongApiKey_Returns401()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", "wrong-key");
        var response = await client.GetAsync("/ws");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ── WebSocket echo ────────────────────────────────────────────────────────

    [Fact]
    public async Task WebSocket_Echo_ReturnsEchoPrefix()
    {
        var wsClient = _factory.Server.CreateWebSocketClient();
        wsClient.ConfigureRequest = req =>
        {
            req.Headers["X-Api-Key"] = WsWebApplicationFactory.TestApiKey;
        };

        var uri = new Uri("ws://localhost/ws");
        using var socket = await wsClient.ConnectAsync(uri, CancellationToken.None);

        Assert.Equal(WebSocketState.Open, socket.State);

        const string message = "Hello WebSocket";
        var sendBuffer = Encoding.UTF8.GetBytes(message);
        await socket.SendAsync(
            new ArraySegment<byte>(sendBuffer),
            WebSocketMessageType.Text,
            endOfMessage: true,
            CancellationToken.None);

        var receiveBuffer = new byte[1024];
        var result = await socket.ReceiveAsync(
            new ArraySegment<byte>(receiveBuffer),
            CancellationToken.None);

        var reply = Encoding.UTF8.GetString(receiveBuffer, 0, result.Count);
        Assert.Equal($"echo: {message}", reply);

        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None);
    }

    [Fact]
    public async Task WebSocket_Echo_MultipleMessages()
    {
        var wsClient = _factory.Server.CreateWebSocketClient();
        wsClient.ConfigureRequest = req =>
        {
            req.Headers["X-Api-Key"] = WsWebApplicationFactory.TestApiKey;
        };

        var uri = new Uri("ws://localhost/ws");
        using var socket = await wsClient.ConnectAsync(uri, CancellationToken.None);

        var messages = new[] { "first", "second", "third" };

        foreach (var msg in messages)
        {
            var sendBuffer = Encoding.UTF8.GetBytes(msg);
            await socket.SendAsync(
                new ArraySegment<byte>(sendBuffer),
                WebSocketMessageType.Text,
                endOfMessage: true,
                CancellationToken.None);

            var receiveBuffer = new byte[1024];
            var result = await socket.ReceiveAsync(
                new ArraySegment<byte>(receiveBuffer),
                CancellationToken.None);

            var reply = Encoding.UTF8.GetString(receiveBuffer, 0, result.Count);
            Assert.Equal($"echo: {msg}", reply);
        }

        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None);
    }

    [Fact]
    public async Task WebSocket_ConnectWithQueryStringApiKey_Succeeds()
    {
        var wsClient = _factory.Server.CreateWebSocketClient();
        // No header — pass key via query string
        var uri = new Uri($"ws://localhost/ws?apiKey={WsWebApplicationFactory.TestApiKey}");
        using var socket = await wsClient.ConnectAsync(uri, CancellationToken.None);

        Assert.Equal(WebSocketState.Open, socket.State);

        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None);
    }
}

using Serilog;
using Serilog.Events;
using WebSocketService.Handlers;
using WebSocketService.Middleware;

#pragma warning disable CA1305 // Serilog Console sink manages its own culture-invariant formatting
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .CreateBootstrapLogger();
#pragma warning restore CA1305

try
{
    var builder = WebApplication.CreateBuilder(args);

    // ── Serilog ───────────────────────────────
    builder.Host.UseSerilog((ctx, services, config) =>
    {
        var isProduction = ctx.HostingEnvironment.IsProduction();
        config
            .ReadFrom.Configuration(ctx.Configuration)
            .ReadFrom.Services(services)
            .Enrich.FromLogContext()
            .Enrich.WithProperty("Service", "WebSocketService")
            .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning);

        if (isProduction)
        {
            config.WriteTo.Console(new Serilog.Formatting.Json.JsonFormatter());
        }
        else
        {
#pragma warning disable CA1305 // Serilog Console sink manages its own culture-invariant formatting
            config.WriteTo.Console(outputTemplate:
                "[{Timestamp:HH:mm:ss} {Level:u3}] {CorrelationId} {Message:lj}{NewLine}{Exception}");
#pragma warning restore CA1305
        }
    });

    // ── WebSocket handler (singleton for connection tracking) ─────────────────
    builder.Services.AddSingleton<WebSocketHandler>();

    var app = builder.Build();

    // ── Graceful shutdown: drain WebSocket connections ────────────────────────
    var lifetime = app.Lifetime;
    var wsHandler = app.Services.GetRequiredService<WebSocketHandler>();
    lifetime.ApplicationStopping.Register(() =>
    {
        // Synchronously wait for drain on the stopping signal
        wsHandler.DrainAsync().GetAwaiter().GetResult();
    });

    app.UseSerilogRequestLogging();

    // API key middleware
    app.UseMiddleware<ApiKeyMiddleware>();

    // Health check — no auth
    app.MapGet("/healthz", () => Results.Ok(new { status = "healthy" }));

    // ── WebSocket endpoint ────────────────────
    app.UseWebSockets(new WebSocketOptions
    {
        KeepAliveInterval = TimeSpan.FromSeconds(30)
    });

    app.Map("/ws", async (HttpContext context, WebSocketHandler handler, CancellationToken ct) =>
    {
        if (!context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsync("WebSocket upgrade required.", CancellationToken.None);
            return;
        }

        var connectionId = Guid.NewGuid();
        var webSocket = await context.WebSockets.AcceptWebSocketAsync();

        var appStopping = app.Lifetime.ApplicationStopping;
        await handler.HandleAsync(connectionId, webSocket, appStopping);
    });

    await app.RunAsync();
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    Log.Fatal(ex, "WebSocketService host terminated unexpectedly");
    return 1;
}
finally
{
    await Log.CloseAndFlushAsync();
}

return 0;

// Make Program accessible to WebApplicationFactory in tests
public partial class Program { }

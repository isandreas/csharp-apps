using System.Net;
using Microsoft.AspNetCore.Http;

namespace WebSocketService.Middleware;

/// <summary>
/// Validates the API key for WebSocket upgrade requests.
/// Accepts the key via query string (?apiKey=) or X-Api-Key header.
/// Skips validation for /healthz.
/// </summary>
public sealed class ApiKeyMiddleware
{
    private const string ApiKeyHeader = "X-Api-Key";
    private const string ApiKeyQuery = "apiKey";
    private readonly RequestDelegate _next;
    private readonly string _apiKey;

    public ApiKeyMiddleware(RequestDelegate next, IConfiguration configuration)
    {
        _next = next;
        _apiKey = configuration["WS_API_KEY"]
            ?? throw new InvalidOperationException("WS_API_KEY environment variable is not configured.");
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? string.Empty;

        if (path.Equals("/healthz", StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

        // Accept key from header or query string (needed during WS upgrade)
        var providedKey = context.Request.Headers[ApiKeyHeader].FirstOrDefault()
            ?? context.Request.Query[ApiKeyQuery].FirstOrDefault();

        if (string.IsNullOrEmpty(providedKey) ||
            !string.Equals(providedKey, _apiKey, StringComparison.Ordinal))
        {
            context.Response.StatusCode = (int)HttpStatusCode.Unauthorized;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsJsonAsync(new
            {
                type = "https://tools.ietf.org/html/rfc7235#section-3.1",
                title = "Unauthorized",
                status = 401,
                detail = "A valid API key must be provided."
            });
            return;
        }

        await _next(context);
    }
}

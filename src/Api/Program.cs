using Api.Data;
using Api.Endpoints;
using Api.Middleware;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Serilog;
using Serilog.Events;

// Bootstrap Serilog immediately so startup errors are captured
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
            .Enrich.WithProperty("Service", "Api")
            .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
            .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning);

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

    // ── EF Core / PostgreSQL ──────────────────
    builder.Services.AddDbContext<AppDbContext>(options =>
        options.UseNpgsql(
            builder.Configuration.GetConnectionString("DefaultConnection"),
            npgsqlOptions => npgsqlOptions.EnableRetryOnFailure(3)));

    // ── Health checks ─────────────────────────
    builder.Services.AddHealthChecks()
        .AddDbContextCheck<AppDbContext>("database");

    // ── OpenAPI / Swagger ─────────────────────
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen(c =>
    {
        c.SwaggerDoc("v1", new() { Title = "CsharpApps API", Version = "v1" });
        c.AddSecurityDefinition("ApiKey", new()
        {
            Type = Microsoft.OpenApi.Models.SecuritySchemeType.ApiKey,
            In = Microsoft.OpenApi.Models.ParameterLocation.Header,
            Name = "X-Api-Key",
            Description = "API key authentication"
        });
        c.AddSecurityRequirement(new()
        {
            {
                new() { Reference = new() { Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme, Id = "ApiKey" } },
                []
            }
        });
    });

    // ── Problem Details ───────────────────────
    builder.Services.AddProblemDetails();

    var app = builder.Build();

    // ── Request pipeline ──────────────────────

    // Correlation ID enrichment
    app.Use(async (context, next) =>
    {
        var correlationId = context.Request.Headers["X-Correlation-ID"].FirstOrDefault()
            ?? Guid.NewGuid().ToString("N");
        context.Response.Headers["X-Correlation-ID"] = correlationId;
        using (Serilog.Context.LogContext.PushProperty("CorrelationId", correlationId))
        {
            await next(context);
        }
    });

    // Global exception handler -> RFC 7807 problem details
    app.UseExceptionHandler(exceptionApp =>
    {
        exceptionApp.Run(async context =>
        {
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            context.Response.ContentType = "application/problem+json";

            var feature = context.Features.Get<IExceptionHandlerFeature>();
            var logger = context.RequestServices.GetRequiredService<ILogger<Program>>();

#pragma warning disable CA1848 // LoggerMessage delegate not applicable in top-level exception handler
            logger.LogError(feature?.Error, "Unhandled exception");
#pragma warning restore CA1848

            await context.Response.WriteAsJsonAsync(new
            {
                type = "https://tools.ietf.org/html/rfc7231#section-6.6.1",
                title = "An unexpected error occurred.",
                status = 500,
                correlationId = context.Response.Headers["X-Correlation-ID"].ToString()
            });
        });
    });

    app.UseSerilogRequestLogging();

    // API key auth (skips /healthz and /swagger/**)
    app.UseMiddleware<ApiKeyMiddleware>();

    // Health check -- no auth
    app.MapHealthChecks("/healthz", new HealthCheckOptions
    {
        ResultStatusCodes =
        {
            [HealthStatus.Healthy] = StatusCodes.Status200OK,
            [HealthStatus.Degraded] = StatusCodes.Status200OK,
            [HealthStatus.Unhealthy] = StatusCodes.Status503ServiceUnavailable
        }
    });

    // Swagger UI (always enabled for demo; restrict in production if needed)
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "CsharpApps API v1");
        c.RoutePrefix = "swagger";
    });

    // API endpoints
    app.MapItemsEndpoints();

    // Auto-migrate on startup in all environments.
    // MigrateAsync is idempotent — already-applied migrations are skipped.
    // IsRelational() guard ensures InMemory provider used in tests is skipped.
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        if (db.Database.IsRelational())
        {
            await db.Database.MigrateAsync();
        }
        else
        {
            await db.Database.EnsureCreatedAsync();
        }
    }

    await app.RunAsync();
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    Log.Fatal(ex, "Api host terminated unexpectedly");
    return 1;
}
finally
{
    await Log.CloseAndFlushAsync();
}

return 0;

// Make Program class accessible to WebApplicationFactory in tests
public partial class Program { }

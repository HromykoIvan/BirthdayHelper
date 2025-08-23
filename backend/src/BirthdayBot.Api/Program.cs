// path: backend/src/BirthdayBot.Api/Program.cs
using BirthdayBot.Api.DI;
using BirthdayBot.Api.Options;
using BirthdayBot.Api.RateLimiting;
using BirthdayBot.Application.Interfaces;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

var builder = WebApplication.CreateBuilder(args);

// JSON logging
builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(o =>
{
    o.IncludeScopes = false;
    o.SingleLine = true;
    o.TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffZ ";
});
builder.Services.AddBotServices(builder.Configuration);
builder.Services.AddWebhookRateLimiting();

builder.Services.AddEndpointsApiExplorer();

var app = builder.Build();

// Health endpoints
app.MapHealthChecks("/health/live");
app.MapHealthChecks("/health/startup");
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = _ => true
});

// Prometheus metrics
var metricsOptions = app.Services.GetRequiredService<IOptions<MetricsOptions>>().Value;
if (metricsOptions.Enable)
{
    app.MapPrometheusScrapingEndpoint(metricsOptions.ScrapeEndpoint);
}

// Telegram webhook endpoint with secret token validation + rate limiting
app.MapPost("/telegram/webhook", async (HttpRequest request, IUpdateHandler handler, IOptions<BotOptions> botOptions, CancellationToken ct) =>
{
    if (!request.Headers.TryGetValue("X-Telegram-Bot-Api-Secret-Token", out var tokenHeader))
        return Results.Unauthorized();

    if (!string.Equals(tokenHeader.ToString(), botOptions.Value.WebhookSecretToken, StringComparison.Ordinal))
        return Results.Unauthorized();

    var update = await request.ReadFromJsonAsync<Update>(cancellationToken: ct);
    if (update is null) return Results.BadRequest();

    // Only handle message/callback query to keep it simple
    if (update.Type == UpdateType.Message || update.Type == UpdateType.CallbackQuery)
    {
        await handler.HandleUpdateAsync(update, ct);
    }

    return Results.Ok();
}).RequireRateLimiting(RateLimitingExtensions.WebhookPolicy);

// Root
app.MapGet("/", () => Results.Ok(new { name = "BirthdayBot", status = "ok" }));

app.Run();

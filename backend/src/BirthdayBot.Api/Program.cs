using BirthdayBot.Api.DI;
using BirthdayBot.Api.Options;
using BirthdayBot.Api.RateLimiting;
using BirthdayBot.Api.Endpoints;
using BirthdayBot.Application.Interfaces;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Telegram.Bot.Types;

var builder = WebApplication.CreateBuilder(args);

// logging
builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(o =>
{
    o.IncludeScopes = false;
    o.SingleLine = true;
    o.TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffZ ";
});

// services
builder.Services.AddBotServices(builder.Configuration);
builder.Services.AddWebhookRateLimiting();
builder.Services.AddEndpointsApiExplorer();

var app = builder.Build();

// health
app.MapHealthChecks("/health/live");
app.MapGet("/healthz", () => Results.Ok("ok"));
app.MapHealthChecks("/health/startup");
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = _ => true });

// metrics
var metricsOptions = app.Services.GetRequiredService<IOptions<MetricsOptions>>().Value;
if (metricsOptions.Enable) app.MapPrometheusScrapingEndpoint(metricsOptions.ScrapeEndpoint);

app.MapTelegramEndpoints();
// root
app.MapGet("/", () => Results.Ok(new { name = "BirthdayBot", status = "ok" }));

app.Run();
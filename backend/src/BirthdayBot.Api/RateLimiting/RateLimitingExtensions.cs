// path: backend/src/BirthdayBot.Api/RateLimiting/RateLimitingExtensions.cs
using System.Threading.RateLimiting;

namespace BirthdayBot.Api.RateLimiting;

public static class RateLimitingExtensions
{
    public const string WebhookPolicy = "webhook-policy";

    public static IServiceCollection AddWebhookRateLimiting(this IServiceCollection services)
    {
        services.AddRateLimiter(opts =>
        {
            opts.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            opts.AddPolicy(WebhookPolicy, httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "anon",
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 60, // 60 req / minute per IP
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0,
                        AutoReplenishment = true
                    }));
        });
        return services;
    }
}
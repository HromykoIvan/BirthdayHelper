using System.Security.Cryptography;
using System.Text;
using BirthdayBot.Api.Options;
using BirthdayBot.Application.Interfaces;
using Microsoft.Extensions.Options;

namespace BirthdayBot.Api.Endpoints;

public static class ReminderEndpoints
{
    private const string SchedulerSecretHeader = "X-BirthdayBot-Scheduler-Secret";

    public static IEndpointRouteBuilder MapReminderEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/internal/reminders/run", async (
            HttpRequest request,
            IReminderService reminderService,
            IOptions<SchedulerOptions> options,
            CancellationToken ct) =>
        {
            var expectedSecret = options.Value.Secret;

            if (string.IsNullOrWhiteSpace(expectedSecret))
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status503ServiceUnavailable,
                    title: "Scheduler endpoint is not configured");
            }

            if (!request.Headers.TryGetValue(SchedulerSecretHeader, out var providedHeader))
            {
                return Results.Unauthorized();
            }

            var providedSecret = providedHeader.ToString();
            if (!SecretsMatch(expectedSecret, providedSecret))
            {
                return Results.Unauthorized();
            }

            await reminderService.RunOnceAsync(ct);

            return Results.Ok(new
            {
                status = "ok",
                executedAtUtc = DateTimeOffset.UtcNow
            });
        });

        return app;
    }

    private static bool SecretsMatch(string expected, string provided)
    {
        var expectedBytes = Encoding.UTF8.GetBytes(expected);
        var providedBytes = Encoding.UTF8.GetBytes(provided);

        return expectedBytes.Length == providedBytes.Length
            && CryptographicOperations.FixedTimeEquals(expectedBytes, providedBytes);
    }
}

using System.Security.Cryptography;
using System.Text;
using BirthdayBot.Application.Interfaces;
using Newtonsoft.Json;
using Telegram.Bot.Types;

namespace BirthdayBot.Api.Endpoints;

public static class TelegramEndpoints
{
    private const string TelegramSecretHeader = "X-Telegram-Bot-Api-Secret-Token";

    public static IEndpointRouteBuilder MapTelegramEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/telegram/webhook", async (
            HttpRequest request,
            IConfiguration cfg,
            IUpdateHandler handler,
            CancellationToken ct) =>
        {
            var secret = cfg["Bot:WebhookSecretToken"];

            if (!string.IsNullOrWhiteSpace(secret))
            {
                if (!request.Headers.TryGetValue(TelegramSecretHeader, out var header) ||
                    !SecretsMatch(secret, header.ToString()))
                {
                    return Results.Unauthorized();
                }
            }

            using var sr = new StreamReader(request.Body);
            var json = await sr.ReadToEndAsync(ct);

            Update? update;
            try
            {
                update = JsonConvert.DeserializeObject<Update>(json);
            }
            catch (Exception ex)
            {
                return Results.BadRequest($"Invalid update payload: {ex.Message}");
            }

            if (update is null)
            {
                return Results.BadRequest("Empty update");
            }

            await handler.HandleUpdateAsync(update, ct);
            return Results.Ok();
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

using BirthdayBot.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using Telegram.Bot.Types;

namespace BirthdayBot.Api.Endpoints;

public static class TelegramEndpoints
{
    public static IEndpointRouteBuilder MapTelegramEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/telegram/webhook", async (
            HttpRequest request, IConfiguration cfg, IUpdateHandler handler, CancellationToken ct) =>
        {
            var secret = cfg["Bot:WebhookSecret"] ?? cfg["BOT__WEBHOOKSECRET"]
                       ?? cfg["BOT__WEBHOOKSECRETTOKEN"] ?? cfg["TELEGRAM_WEBHOOK_SECRET"];

            if (!string.IsNullOrEmpty(secret))
            {
                if (!request.Headers.TryGetValue("X-Telegram-Bot-Api-Secret-Token", out var header) || header != secret)
                    return Results.Unauthorized();
            }

            using var sr = new StreamReader(request.Body);
            var json = await sr.ReadToEndAsync(ct);

            Update? update;
            try { update = JsonConvert.DeserializeObject<Update>(json); }
            catch (Exception ex) { return Results.BadRequest($"Invalid update payload: {ex.Message}"); }

            if (update is null) return Results.BadRequest("Empty update");

            await handler.HandleUpdateAsync(update, ct);
            return Results.Ok();
        });

        return app;
    }
}
using BirthdayBot.Application.Interfaces;
using BirthdayBot.Infrastructure.Google;
using Telegram.Bot;

namespace BirthdayBot.Api.Endpoints;

public static class GoogleAuthEndpoints
{
    private sealed class Marker { }  // logger category marker for static endpoint class

    public static IEndpointRouteBuilder MapGoogleAuthEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/auth/google/callback", async (
            HttpContext context,
            string? code,
            string? state,
            string? error,
            PendingGoogleAuthStore pendingStore,
            IGoogleImportService importService,
            ITelegramBotClient bot,
            ILocalizationService i18n,
            ILogger<Marker> log,
            CancellationToken ct) =>
        {
            // Google redirected with an error (user declined)
            if (!string.IsNullOrEmpty(error))
            {
                log.LogInformation("Google OAuth cancelled by user. error={Error}", error);
                return Results.Content(HtmlPage(
                    title: "Access denied",
                    heading: "Access denied",
                    body: "You did not grant access to your contacts. Return to Telegram and try again.",
                    emoji: "❌"), "text/html");
            }

            if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(state))
            {
                log.LogWarning("Google callback: missing code or state");
                return Results.Content(HtmlPage(
                    title: "Invalid request",
                    heading: "Something went wrong",
                    body: "The authorization link is invalid or has expired. Return to Telegram and try again.",
                    emoji: "⚠️"), "text/html");
            }

            if (!pendingStore.TryGet(state, out var entry))
            {
                log.LogWarning("Google callback: state={State} not found (expired or reused)", state);
                return Results.Content(HtmlPage(
                    title: "Link expired",
                    heading: "Link expired",
                    body: "The link has expired or was already used. Return to Telegram and start again.",
                    emoji: "⏱"), "text/html");
            }

            pendingStore.Remove(state);

            var result = await importService.ImportContactsAsync(code, entry.TelegramUserId, ct);

            // Resolve user language for the Telegram message
            var user = await GetUserSafe(context.RequestServices, entry.TelegramUserId, ct);
            var lang = user?.Lang ?? BirthdayBot.Domain.Enums.Language.En;

            string telegramText;
            string htmlHeading;
            string htmlBody;

            if (!result.IsSuccess)
            {
                telegramText = i18n.GetText(lang, "import_google_error");
                htmlHeading  = "Import failed";
                htmlBody     = "An error occurred during import. Return to Telegram for details.";
            }
            else if (result.Total == 0)
            {
                telegramText = i18n.GetText(lang, "import_google_empty");
                htmlHeading  = "No contacts found";
                htmlBody     = "No contacts with birthdays were found in your Google account.";
            }
            else
            {
                telegramText = string.Format(
                    i18n.GetText(lang, "import_google_success"),
                    result.Imported,
                    result.Skipped);
                htmlHeading = "Import complete";
                htmlBody    = $"Imported {result.Imported} contact(s). You can return to Telegram now.";
            }

            // Send Telegram message to the user's chat
            try
            {
                await bot.SendTextMessageAsync(
                    entry.ChatId,
                    telegramText,
                    parseMode: Telegram.Bot.Types.Enums.ParseMode.Html,
                    cancellationToken: ct);
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Failed to send Telegram import result to chat {ChatId}", entry.ChatId);
            }

            return Results.Content(
                HtmlPage(title: htmlHeading, heading: htmlHeading, body: htmlBody, emoji: result.IsSuccess ? "✅" : "⚠️"),
                "text/html");
        });

        return app;
    }

    private static async Task<BirthdayBot.Domain.Entities.User?> GetUserSafe(
        IServiceProvider sp, long telegramUserId, CancellationToken ct)
    {
        try
        {
            var repo = sp.GetRequiredService<IUserRepository>();
            return await repo.GetByTelegramUserIdAsync(telegramUserId, ct);
        }
        catch { return null; }
    }

    private static string HtmlPage(string title, string heading, string body, string emoji)
    {
        return "<!DOCTYPE html><html lang=\"en\"><head>" +
               "<meta charset=\"utf-8\">" +
               "<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">" +
               $"<title>{title}</title>" +
               "<style>" +
               "body{font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',sans-serif;" +
               "display:flex;align-items:center;justify-content:center;" +
               "min-height:100vh;margin:0;background:#f5f5f5}" +
               ".card{background:white;border-radius:16px;padding:2.5rem 2rem;" +
               "max-width:400px;text-align:center;box-shadow:0 4px 20px rgba(0,0,0,.08)}" +
               ".emoji{font-size:3rem;margin-bottom:1rem}" +
               "h1{margin:0 0 .75rem;font-size:1.4rem;color:#1a1a1a}" +
               "p{margin:0;color:#555;line-height:1.6}" +
               "</style></head><body>" +
               "<div class=\"card\">" +
               $"<div class=\"emoji\">{emoji}</div>" +
               $"<h1>{heading}</h1>" +
               $"<p>{body}</p>" +
               "</div></body></html>";
    }
}

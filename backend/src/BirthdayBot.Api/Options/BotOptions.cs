// path: backend/src/BirthdayBot.Api/Options/BotOptions.cs
namespace BirthdayBot.Api.Options;

public class BotOptions
{
    public string Token { get; set; } = "";
    /// <summary>
    /// Secret token to validate Telegram webhook requests via X-Telegram-Bot-Api-Secret-Token
    /// </summary>
    public string WebhookSecretToken { get; set; } = "";
}
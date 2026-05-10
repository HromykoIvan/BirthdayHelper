namespace BirthdayBot.Infrastructure.Google;

public sealed class GoogleOptions
{
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>Base URL of this service, used to build the OAuth callback URL.
    /// Example: https://bot.example.com
    /// </summary>
    public string CallbackBaseUrl { get; set; } = string.Empty;

    public string CallbackUrl => $"{CallbackBaseUrl.TrimEnd('/')}/auth/google/callback";
}

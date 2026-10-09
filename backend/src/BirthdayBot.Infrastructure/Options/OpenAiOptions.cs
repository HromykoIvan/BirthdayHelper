namespace BirthdayBot.Infrastructure.Options;

public sealed class OpenAiOptions
{
    public bool Enable { get; set; } = false;
    public string ApiKey { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = "https://api.openai.com/v1/";
    public string IntentModel { get; set; } = "gpt-6-luna";
    public string GreetingModel { get; set; } = "gpt-6-luna";
    public int TimeoutSeconds { get; set; } = 20;
    public int MaxOutputTokens { get; set; } = 900;

    public bool IsConfigured =>
        Enable &&
        !string.IsNullOrWhiteSpace(ApiKey) &&
        !ApiKey.StartsWith("REPLACE_", StringComparison.OrdinalIgnoreCase);
}

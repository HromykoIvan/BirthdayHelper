namespace BirthdayBot.Infrastructure.Options;

public sealed class PromptProfileOptions
{
    public string IntentPromptVersion { get; set; } = "intent-v1";
    public string GreetingPromptVersion { get; set; } = "greeting-v1";
}

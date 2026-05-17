namespace BirthdayBot.Infrastructure.Options;

public sealed class LocalAiOptions
{
    public bool Enable { get; set; } = true;
    public bool UseOllama { get; set; } = false;
    public string BaseUrl { get; set; } = "http://ollama:11434";
    public string Model { get; set; } = "qwen2.5:7b-instruct";
    public int TimeoutSeconds { get; set; } = 8;
    public int MaxPromptChars { get; set; } = 1200;
}

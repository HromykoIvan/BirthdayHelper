namespace BirthdayBot.Application.Models;

public sealed record AiGreetingVariant(string Style, string Text);

public sealed record AiEnhanceResult(
    string Text,
    bool IsFallback,
    string PromptVersion,
    string ModelSource,
    string? FallbackReason = null,
    double? LatencyMs = null,
    IReadOnlyList<AiGreetingVariant>? Variants = null,
    int? InputTokens = null,
    int? OutputTokens = null);

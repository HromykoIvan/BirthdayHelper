namespace BirthdayBot.Application.Models;

public sealed record AiEnhanceResult(
    string Text,
    bool IsFallback,
    string PromptVersion,
    string ModelSource,
    string? FallbackReason = null,
    double? LatencyMs = null);

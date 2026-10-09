using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;
using BirthdayBot.Application.Interfaces;
using BirthdayBot.Application.Models;
using BirthdayBot.Domain.Entities;
using BirthdayBot.Domain.Enums;
using BirthdayBot.Infrastructure.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BirthdayBot.Infrastructure.Services;

/// <summary>
/// Generates three useful greeting styles in one inexpensive OpenAI call.
/// Falls back to the deterministic draft if OpenAI is unavailable.
/// </summary>
public sealed class OpenAiGreetingEnhancer : IAiGreetingEnhancer
{
    private static readonly JsonObject GreetingSchema = new()
    {
        ["type"] = "object",
        ["additionalProperties"] = false,
        ["properties"] = new JsonObject
        {
            ["warm"] = new JsonObject { ["type"] = "string" },
            ["short"] = new JsonObject { ["type"] = "string" },
            ["personal"] = new JsonObject { ["type"] = "string" }
        },
        ["required"] = new JsonArray("warm", "short", "personal")
    };

    private readonly OpenAiResponsesClient _client;
    private readonly IAiEventRepository _aiEvents;
    private readonly OpenAiOptions _options;
    private readonly PromptProfileOptions _promptProfiles;
    private readonly AiMetrics _metrics;
    private readonly ILogger<OpenAiGreetingEnhancer> _logger;

    public OpenAiGreetingEnhancer(
        OpenAiResponsesClient client,
        IAiEventRepository aiEvents,
        IOptions<OpenAiOptions> options,
        IOptions<PromptProfileOptions> promptProfiles,
        AiMetrics metrics,
        ILogger<OpenAiGreetingEnhancer> logger)
    {
        _client = client;
        _aiEvents = aiEvents;
        _options = options.Value;
        _promptProfiles = promptProfiles.Value;
        _metrics = metrics;
        _logger = logger;
    }

    public async Task<AiEnhanceResult> EnhanceAsync(
        User user,
        Birthday birthday,
        string draftGreeting,
        int? age,
        CancellationToken ct = default)
    {
        const string source = "openai";
        var sw = Stopwatch.StartNew();
        _metrics.TrackEnhanceRequest(source);

        if (!_client.IsConfigured)
        {
            _metrics.TrackEnhanceFallback("openai_disabled");
            return Fallback(draftGreeting, "openai_disabled");
        }

        try
        {
            var examples = await _aiEvents.ListAcceptedGreetingExamplesAsync(user.Id, 3, ct);
            var instructions = BuildInstructions(user, birthday, age, examples.Select(x => x.OutputText!).ToArray());

            var result = await _client.GenerateStructuredAsync<GreetingEnvelope>(
                _options.GreetingModel,
                instructions,
                draftGreeting,
                "birthday_greeting_variants",
                GreetingSchema,
                ct);

            if (result is null)
            {
                _metrics.TrackEnhanceFallback("openai_unavailable");
                return Fallback(draftGreeting, "openai_unavailable");
            }

            var variants = new[]
            {
                new AiGreetingVariant("warm", result.Value.Warm.Trim()),
                new AiGreetingVariant("short", result.Value.Short.Trim()),
                new AiGreetingVariant("personal", result.Value.Personal.Trim())
            }
            .Where(x => !string.IsNullOrWhiteSpace(x.Text))
            .ToArray();

            if (variants.Length == 0)
            {
                _metrics.TrackEnhanceFallback("openai_empty");
                return Fallback(draftGreeting, "openai_empty");
            }

            var preferred = variants.FirstOrDefault(x => x.Style == "personal")?.Text
                            ?? variants[0].Text;

            return new AiEnhanceResult(
                preferred,
                IsFallback: false,
                PromptVersion: _promptProfiles.GreetingPromptVersion,
                ModelSource: result.Model,
                LatencyMs: sw.Elapsed.TotalMilliseconds,
                Variants: variants,
                InputTokens: result.InputTokens,
                OutputTokens: result.OutputTokens);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "OpenAI greeting generation failed; using fallback draft.");
            _metrics.TrackEnhanceFallback("openai_exception");
            return Fallback(draftGreeting, "openai_exception");
        }
        finally
        {
            sw.Stop();
            _metrics.TrackEnhanceLatency(sw.Elapsed.TotalMilliseconds, source);
        }
    }

    private AiEnhanceResult Fallback(string draft, string reason) =>
        new(
            draft,
            IsFallback: true,
            PromptVersion: _promptProfiles.GreetingPromptVersion,
            ModelSource: "template",
            FallbackReason: reason);

    private static string BuildInstructions(
        User user,
        Birthday birthday,
        int? age,
        IReadOnlyList<string> acceptedExamples)
    {
        var language = (birthday.GreetingLanguage ?? user.Lang) switch
        {
            Language.Ru => "Russian",
            Language.Pl => "Polish",
            _ => "English"
        };

        var tone = user.Tone == Tone.Formal ? "formal/respectful" : "friendly/natural";
        var examples = acceptedExamples.Count == 0
            ? "No accepted examples yet."
            : string.Join("\n---\n", acceptedExamples.Select((x, i) => $"Example {i + 1}: {x}"));

        var context = new StringBuilder()
            .AppendLine($"Recipient: {birthday.FullName}")
            .AppendLine($"Language: {language}")
            .AppendLine($"Tone: {tone}")
            .AppendLine($"Age: {(age.HasValue ? age.Value.ToString() : "unknown")}")
            .AppendLine($"Relation: {birthday.Relation ?? "unknown"}")
            .AppendLine($"Profession: {birthday.Profession ?? "unknown"}")
            .AppendLine($"Interests: {birthday.Interests ?? "unknown"}")
            .AppendLine($"Notes: {birthday.Notes ?? "none"}")
            .ToString();

        return $"""
            You write birthday greetings for a personal reminder assistant.

            Generate exactly three variants:
            - warm: warm and sincere, normally 2-4 sentences.
            - short: natural and concise, normally 1-2 sentences.
            - personal: the most personalized version using only facts provided below.

            Rules:
            - Write in {language}.
            - Tone should be {tone}.
            - Address the recipient naturally by name.
            - Do not expose metadata labels such as "relation", "profession", or "interests".
            - Never invent facts.
            - If age is unknown, do not mention age.
            - If age is known, mention it only when it genuinely improves the greeting; prefer milestones.
            - Use interests/profession only when they fit naturally.
            - Avoid generic corporate clichés and exaggerated sentiment.
            - Do not mention that AI generated the text.
            - Do not include headings such as "Warm:" inside the greeting itself.

            Context:
            {context}

            User-approved examples of preferred style:
            {examples}
            """;
    }

    private sealed class GreetingEnvelope
    {
        public string Warm { get; set; } = string.Empty;
        public string Short { get; set; } = string.Empty;
        public string Personal { get; set; } = string.Empty;
    }
}

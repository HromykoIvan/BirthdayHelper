using System.Diagnostics;
using System.Net.Http.Json;
using System.Text;
using BirthdayBot.Application.Interfaces;
using BirthdayBot.Application.Models;
using BirthdayBot.Domain.Entities;
using BirthdayBot.Domain.Enums;
using BirthdayBot.Infrastructure.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BirthdayBot.Infrastructure.Services;

public sealed class LocalAiGreetingEnhancer : IAiGreetingEnhancer
{
    private readonly IAiEventRepository _aiEvents;
    private readonly LocalAiOptions _options;
    private readonly PromptProfileOptions _promptProfiles;
    private readonly AiMetrics _metrics;
    private readonly ILogger<LocalAiGreetingEnhancer> _logger;

    public LocalAiGreetingEnhancer(
        IAiEventRepository aiEvents,
        IOptions<LocalAiOptions> options,
        IOptions<PromptProfileOptions> promptProfiles,
        AiMetrics metrics,
        ILogger<LocalAiGreetingEnhancer> logger)
    {
        _aiEvents = aiEvents;
        _options = options.Value;
        _promptProfiles = promptProfiles.Value;
        _metrics = metrics;
        _logger = logger;
    }

    public async Task<AiEnhanceResult> EnhanceAsync(User user, Birthday birthday, string draftGreeting, int age, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        var source = _options.UseOllama ? "ollama" : "local-template";
        _metrics.TrackEnhanceRequest(source);

        try
        {
            if (!_options.Enable)
            {
                _metrics.TrackEnhanceFallback("disabled");
                return new AiEnhanceResult(
                    draftGreeting,
                    IsFallback: true,
                    PromptVersion: _promptProfiles.GreetingPromptVersion,
                    ModelSource: source,
                    FallbackReason: "disabled");
            }

            var personalized = BuildPersonalizedDraft(user, birthday, draftGreeting, age);

            if (!_options.UseOllama)
            {
                return new AiEnhanceResult(
                    personalized,
                    IsFallback: false,
                    PromptVersion: _promptProfiles.GreetingPromptVersion,
                    ModelSource: source);
            }

            var examples = await _aiEvents.ListAcceptedGreetingExamplesAsync(user.Id, 3, ct);
            var prompt = BuildPrompt(user.Lang, birthday, personalized, age, _promptProfiles.GreetingPromptVersion, examples.Select(x => x.OutputText!).ToArray());
            if (prompt.Length > _options.MaxPromptChars)
            {
                prompt = prompt[.._options.MaxPromptChars];
            }

            using var client = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(Math.Clamp(_options.TimeoutSeconds, 3, 30)),
                BaseAddress = new Uri(_options.BaseUrl)
            };

            var response = await client.PostAsJsonAsync(
                "/api/generate",
                new OllamaRequest(_options.Model, prompt, stream: false),
                ct);

            if (!response.IsSuccessStatusCode)
            {
                _metrics.TrackEnhanceFallback("ollama_http");
                return new AiEnhanceResult(
                    personalized,
                    IsFallback: true,
                    PromptVersion: _promptProfiles.GreetingPromptVersion,
                    ModelSource: source,
                    FallbackReason: "ollama_http");
            }

            var payload = await response.Content.ReadFromJsonAsync<OllamaResponse>(cancellationToken: ct);
            var text = payload?.Response?.Trim();
            if (string.IsNullOrWhiteSpace(text))
            {
                _metrics.TrackEnhanceFallback("ollama_empty");
                return new AiEnhanceResult(
                    personalized,
                    IsFallback: true,
                    PromptVersion: _promptProfiles.GreetingPromptVersion,
                    ModelSource: source,
                    FallbackReason: "ollama_empty");
            }

            return new AiEnhanceResult(
                text,
                IsFallback: false,
                PromptVersion: _promptProfiles.GreetingPromptVersion,
                ModelSource: source);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Local AI greeting enhance failed, fallback to draft.");
            _metrics.TrackEnhanceFallback("exception");
            return new AiEnhanceResult(
                draftGreeting,
                IsFallback: true,
                PromptVersion: _promptProfiles.GreetingPromptVersion,
                ModelSource: source,
                FallbackReason: "exception");
        }
        finally
        {
            _metrics.TrackEnhanceLatency(sw.Elapsed.TotalMilliseconds, source);
        }
    }

    private static string BuildPersonalizedDraft(User user, Birthday birthday, string draftGreeting, int age)
    {
        var sb = new StringBuilder();
        sb.AppendLine(draftGreeting);

        if (!string.IsNullOrWhiteSpace(birthday.Relation))
        {
            sb.AppendLine();
            sb.AppendLine(user.Lang switch
            {
                Language.Ru => $"P.S. Это ваш {birthday.Relation.ToLowerInvariant()}.",
                Language.Pl => $"P.S. To Twoja relacja: {birthday.Relation.ToLowerInvariant()}.",
                _ => $"P.S. This person is your {birthday.Relation.ToLowerInvariant()}."
            });
        }

        if (!string.IsNullOrWhiteSpace(birthday.Interests))
        {
            sb.AppendLine(user.Lang switch
            {
                Language.Ru => $"Добавь что-то про интересы: {birthday.Interests}.",
                Language.Pl => $"Dodaj nawiązanie do zainteresowań: {birthday.Interests}.",
                _ => $"Mention interests: {birthday.Interests}."
            });
        }

        if (!string.IsNullOrWhiteSpace(birthday.Notes))
        {
            sb.AppendLine(user.Lang switch
            {
                Language.Ru => $"Контекст из заметок: {birthday.Notes}.",
                Language.Pl => $"Kontekst z notatek: {birthday.Notes}.",
                _ => $"Context from notes: {birthday.Notes}."
            });
        }

        return sb.ToString().Trim();
    }

    private static string BuildPrompt(Language lang, Birthday birthday, string draft, int age, string promptVersion, string[] acceptedExamples)
    {
        var localeHint = lang switch
        {
            Language.Ru => "Russian",
            Language.Pl => "Polish",
            _ => "English"
        };

        var examplesBlock = acceptedExamples.Length == 0
            ? "No accepted examples yet."
            : string.Join("\n---\n", acceptedExamples.Select((x, i) => $"Example {i + 1}:\n{x}"));

        return $"""
        You are an assistant that rewrites birthday wishes.
        Requirements:
        - Prompt profile version: {promptVersion}
        - Keep language: {localeHint}
        - Keep tone warm and concise (2-4 sentences)
        - Mention recipient by name
        - Keep age reference ({age})
        - Do not add unsafe or sensitive content

        Recipient name: {birthday.FullName}
        Relation: {birthday.Relation ?? "unknown"}
        Interests: {birthday.Interests ?? "n/a"}
        Notes: {birthday.Notes ?? "n/a"}

        User accepted examples:
        {examplesBlock}

        Draft:
        {draft}
        """;
    }

    private sealed record OllamaRequest(string model, string prompt, bool stream);

    private sealed class OllamaResponse
    {
        public string? Response { get; set; }
    }
}

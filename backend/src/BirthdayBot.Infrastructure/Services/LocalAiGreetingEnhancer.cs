using System.Diagnostics;
using System.Net.Http.Json;
using System.Text;
using BirthdayBot.Application.Interfaces;
using BirthdayBot.Domain.Entities;
using BirthdayBot.Domain.Enums;
using BirthdayBot.Infrastructure.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BirthdayBot.Infrastructure.Services;

public sealed class LocalAiGreetingEnhancer : IAiGreetingEnhancer
{
    private readonly LocalAiOptions _options;
    private readonly AiMetrics _metrics;
    private readonly ILogger<LocalAiGreetingEnhancer> _logger;

    public LocalAiGreetingEnhancer(
        IOptions<LocalAiOptions> options,
        AiMetrics metrics,
        ILogger<LocalAiGreetingEnhancer> logger)
    {
        _options = options.Value;
        _metrics = metrics;
        _logger = logger;
    }

    public async Task<string> EnhanceAsync(User user, Birthday birthday, string draftGreeting, int age, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        _metrics.TrackEnhanceRequest(_options.UseOllama ? "ollama" : "local-template");

        try
        {
            if (!_options.Enable)
            {
                _metrics.TrackEnhanceFallback("disabled");
                return draftGreeting;
            }

            var personalized = BuildPersonalizedDraft(user, birthday, draftGreeting, age);

            if (!_options.UseOllama)
            {
                return personalized;
            }

            var prompt = BuildPrompt(user.Lang, birthday, personalized, age);
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
                return personalized;
            }

            var payload = await response.Content.ReadFromJsonAsync<OllamaResponse>(cancellationToken: ct);
            var text = payload?.Response?.Trim();
            if (string.IsNullOrWhiteSpace(text))
            {
                _metrics.TrackEnhanceFallback("ollama_empty");
                return personalized;
            }

            return text;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Local AI greeting enhance failed, fallback to draft.");
            _metrics.TrackEnhanceFallback("exception");
            return draftGreeting;
        }
        finally
        {
            _metrics.TrackEnhanceLatency(sw.Elapsed.TotalMilliseconds, _options.UseOllama ? "ollama" : "local-template");
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

    private static string BuildPrompt(Language lang, Birthday birthday, string draft, int age)
    {
        var localeHint = lang switch
        {
            Language.Ru => "Russian",
            Language.Pl => "Polish",
            _ => "English"
        };

        return $"""
        You are an assistant that rewrites birthday wishes.
        Requirements:
        - Keep language: {localeHint}
        - Keep tone warm and concise (2-4 sentences)
        - Mention recipient by name
        - Keep age reference ({age})
        - Do not add unsafe or sensitive content

        Recipient name: {birthday.FullName}
        Relation: {birthday.Relation ?? "unknown"}
        Interests: {birthday.Interests ?? "n/a"}
        Notes: {birthday.Notes ?? "n/a"}

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

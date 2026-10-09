using System.Text.Json.Nodes;
using BirthdayBot.Application.Interfaces;
using BirthdayBot.Application.Models;
using BirthdayBot.Domain.Entities;
using BirthdayBot.Infrastructure.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BirthdayBot.Infrastructure.Services;

/// <summary>
/// Natural-language intent router backed by OpenAI Structured Outputs.
/// Falls back to the deterministic local router for availability and simple commands.
/// </summary>
public sealed class OpenAiIntentRouter : IIntentRouter
{
    private static readonly JsonObject IntentSchema = new()
    {
        ["type"] = "object",
        ["additionalProperties"] = false,
        ["properties"] = new JsonObject
        {
            ["intent"] = new JsonObject
            {
                ["type"] = "string",
                ["enum"] = new JsonArray(
                    "none",
                    "open_help",
                    "open_settings",
                    "open_add_birthday",
                    "open_list",
                    "remove_by_name",
                    "generate_greeting",
                    "add_birthday",
                    "find_birthday")
            },
            ["entity_name"] = new JsonObject { ["type"] = "string" },
            ["occasion"] = new JsonObject { ["type"] = "string" },
            ["first_name"] = new JsonObject { ["type"] = "string" },
            ["last_name"] = new JsonObject { ["type"] = "string" },
            ["day"] = new JsonObject { ["type"] = "integer", ["minimum"] = 0, ["maximum"] = 31 },
            ["month"] = new JsonObject { ["type"] = "integer", ["minimum"] = 0, ["maximum"] = 12 },
            ["year"] = new JsonObject { ["type"] = "integer", ["minimum"] = 0, ["maximum"] = 2100 },
            ["relation"] = new JsonObject { ["type"] = "string" },
            ["profession"] = new JsonObject { ["type"] = "string" },
            ["interests"] = new JsonObject
            {
                ["type"] = "array",
                ["items"] = new JsonObject { ["type"] = "string" }
            },
            ["notes"] = new JsonObject { ["type"] = "string" },
            ["confidence"] = new JsonObject { ["type"] = "number", ["minimum"] = 0, ["maximum"] = 1 },
            ["requires_confirmation"] = new JsonObject { ["type"] = "boolean" }
        },
        ["required"] = new JsonArray(
            "intent",
            "entity_name",
            "occasion",
            "first_name",
            "last_name",
            "day",
            "month",
            "year",
            "relation",
            "profession",
            "interests",
            "notes",
            "confidence",
            "requires_confirmation")
    };

    private readonly OpenAiResponsesClient _client;
    private readonly LocalIntentRouter _fallback;
    private readonly OpenAiOptions _options;
    private readonly AiMetrics _metrics;
    private readonly ILogger<OpenAiIntentRouter> _logger;

    public OpenAiIntentRouter(
        OpenAiResponsesClient client,
        LocalIntentRouter fallback,
        IOptions<OpenAiOptions> options,
        AiMetrics metrics,
        ILogger<OpenAiIntentRouter> logger)
    {
        _client = client;
        _fallback = fallback;
        _options = options.Value;
        _metrics = metrics;
        _logger = logger;
    }

    public async Task<IntentParseResult> ParseAsync(User user, string input, CancellationToken ct = default)
    {
        if (!_client.IsConfigured || string.IsNullOrWhiteSpace(input))
        {
            return await _fallback.ParseAsync(user, input, ct);
        }

        _metrics.TrackIntentRequest("openai");
        var started = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            var language = user.Lang.ToString();
            var instructions = $"""
                You classify messages for a birthday reminder Telegram bot.
                Interface language is {language}.

                Supported intents:
                - add_birthday: the message itself contains a person's name and a birthday date.
                - find_birthday: asks when a specific person's birthday is.
                - remove_by_name: asks to delete a person.
                - generate_greeting: asks to write/generate a greeting for a named person or occasion.
                - open_add_birthday: wants to add a birthday but did not provide enough date information.
                - open_list: asks to see birthdays/list/upcoming birthdays.
                - open_settings, open_help.
                - none.

                Extraction rules:
                - Never invent a birth year. Use year=0 if the year was not provided.
                - Never invent relation, profession, interests, or notes. Use empty values when absent.
                - Split a person's name into first_name/last_name when reasonably clear.
                - day=0 and month=0 if no birthday date was supplied.
                - entity_name should contain the person's name when relevant.
                - For add/remove actions, requires_confirmation must be true.
                - Treat common birthday abbreviations such as "др" and multilingual equivalents naturally.
                """;

            var result = await _client.GenerateStructuredAsync<IntentEnvelope>(
                _options.IntentModel,
                instructions,
                input,
                "birthday_bot_intent",
                IntentSchema,
                ct);

            if (result is null)
            {
                _metrics.TrackIntentFallback("openai_unavailable");
                return await _fallback.ParseAsync(user, input, ct);
            }

            var mapped = Map(result.Value, result);
            if (mapped.Intent == UserIntentType.None && mapped.Confidence < 0.65)
            {
                var local = await _fallback.ParseAsync(user, input, ct);
                if (local.Intent != UserIntentType.None)
                {
                    return local;
                }
            }

            return mapped;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "OpenAI intent routing failed; using local fallback.");
            _metrics.TrackIntentFallback("openai_exception");
            return await _fallback.ParseAsync(user, input, ct);
        }
        finally
        {
            started.Stop();
            _metrics.TrackIntentLatency(started.Elapsed.TotalMilliseconds, "openai");
        }
    }

    private static IntentParseResult Map(
        IntentEnvelope x,
        OpenAiCallResult<IntentEnvelope> call)
    {
        var confidence = Math.Clamp(x.Confidence, 0, 1);
        var common = new
        {
            Source = call.Model,
            call.InputTokens,
            call.OutputTokens
        };

        return x.Intent switch
        {
            "open_help" => new IntentParseResult(
                UserIntentType.OpenHelp,
                Confidence: confidence,
                ModelSource: common.Source,
                InputTokens: common.InputTokens,
                OutputTokens: common.OutputTokens),

            "open_settings" => new IntentParseResult(
                UserIntentType.OpenSettings,
                Confidence: confidence,
                ModelSource: common.Source,
                InputTokens: common.InputTokens,
                OutputTokens: common.OutputTokens),

            "open_add_birthday" => new IntentParseResult(
                UserIntentType.OpenAddBirthday,
                Confidence: confidence,
                ModelSource: common.Source,
                InputTokens: common.InputTokens,
                OutputTokens: common.OutputTokens),

            "open_list" => new IntentParseResult(
                UserIntentType.OpenList,
                Confidence: confidence,
                ModelSource: common.Source,
                InputTokens: common.InputTokens,
                OutputTokens: common.OutputTokens),

            "remove_by_name" when !string.IsNullOrWhiteSpace(x.EntityName) => new IntentParseResult(
                UserIntentType.RemoveByName,
                EntityName: x.EntityName.Trim(),
                RequiresConfirmation: true,
                Confidence: confidence,
                ModelSource: common.Source,
                InputTokens: common.InputTokens,
                OutputTokens: common.OutputTokens),

            "generate_greeting" when !string.IsNullOrWhiteSpace(x.EntityName) => new IntentParseResult(
                UserIntentType.GenerateGreetingPreview,
                EntityName: x.EntityName.Trim(),
                Occasion: string.IsNullOrWhiteSpace(x.Occasion) ? "birthday" : x.Occasion.Trim(),
                Confidence: confidence,
                ModelSource: common.Source,
                InputTokens: common.InputTokens,
                OutputTokens: common.OutputTokens),

            "find_birthday" when !string.IsNullOrWhiteSpace(x.EntityName) => new IntentParseResult(
                UserIntentType.FindBirthday,
                EntityName: x.EntityName.Trim(),
                Confidence: confidence,
                ModelSource: common.Source,
                InputTokens: common.InputTokens,
                OutputTokens: common.OutputTokens),

            "add_birthday" when IsValidDate(x.Day, x.Month, x.Year) && !string.IsNullOrWhiteSpace(x.FirstName) =>
                new IntentParseResult(
                    UserIntentType.AddBirthdayFromText,
                    EntityName: x.EntityName,
                    Birthday: new BirthdayDraft(
                        x.FirstName.Trim(),
                        EmptyToNull(x.LastName),
                        x.Day,
                        x.Month,
                        x.Year > 0 ? x.Year : null,
                        EmptyToNull(x.Relation),
                        EmptyToNull(x.Profession),
                        x.Interests.Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v.Trim()).ToArray(),
                        EmptyToNull(x.Notes)),
                    RequiresConfirmation: true,
                    Confidence: confidence,
                    ModelSource: common.Source,
                    InputTokens: common.InputTokens,
                    OutputTokens: common.OutputTokens),

            "add_birthday" => new IntentParseResult(
                UserIntentType.OpenAddBirthday,
                Confidence: confidence,
                ModelSource: common.Source,
                InputTokens: common.InputTokens,
                OutputTokens: common.OutputTokens),

            _ => new IntentParseResult(
                UserIntentType.None,
                Confidence: confidence,
                ModelSource: common.Source,
                InputTokens: common.InputTokens,
                OutputTokens: common.OutputTokens)
        };
    }

    private static bool IsValidDate(int day, int month, int year)
    {
        if (month is < 1 or > 12 || day < 1)
        {
            return false;
        }

        var safeYear = year > 0 ? year : 2000;
        return day <= DateTime.DaysInMonth(safeYear, month);
    }

    private static string? EmptyToNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private sealed class IntentEnvelope
    {
        public string Intent { get; set; } = "none";
        public string EntityName { get; set; } = string.Empty;
        public string Occasion { get; set; } = string.Empty;
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public int Day { get; set; }
        public int Month { get; set; }
        public int Year { get; set; }
        public string Relation { get; set; } = string.Empty;
        public string Profession { get; set; } = string.Empty;
        public string[] Interests { get; set; } = [];
        public string Notes { get; set; } = string.Empty;
        public double Confidence { get; set; }
        public bool RequiresConfirmation { get; set; }
    }
}

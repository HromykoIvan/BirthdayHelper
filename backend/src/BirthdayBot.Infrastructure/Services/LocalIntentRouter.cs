using System.Diagnostics;
using System.Text.RegularExpressions;
using BirthdayBot.Application.Interfaces;
using BirthdayBot.Application.Models;
using BirthdayBot.Domain.Entities;
using BirthdayBot.Domain.Enums;
using BirthdayBot.Domain.Utils;
using NodaTime;

namespace BirthdayBot.Infrastructure.Services;

public sealed class LocalIntentRouter : IIntentRouter
{
    private static readonly Regex RemoveRegex = new(
        @"^(удали|удалить|remove|delete|usun|usu[ńn])\s+(?<name>.+)$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex GreetingPreviewRegex = new(
        @"^(сгенерируй|создай|generate)\s+(поздравление|greeting)\s+для\s+(?<name>.+?)\s+на\s+(?<occasion>.+)$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private readonly IDateTimeZoneProvider _tzdb;
    private readonly AiMetrics _metrics;

    public LocalIntentRouter(AiMetrics metrics, IDateTimeZoneProvider? tzdb = null)
    {
        _metrics = metrics;
        _tzdb = tzdb ?? DateTimeZoneProviders.Tzdb;
    }

    public Task<IntentParseResult> ParseAsync(User user, string input, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        _metrics.TrackIntentRequest("local");

        try
        {
            if (string.IsNullOrWhiteSpace(input))
            {
                _metrics.TrackIntentFallback("empty");
                return Task.FromResult(IntentParseResult.NoMatch);
            }

            var text = input.Trim();
            var lowered = text.ToLowerInvariant();

            if (ContainsAny(lowered, "помощь", "help", "pomoc"))
            {
                return Task.FromResult(IntentParseResult.ForNavigation(UserIntentType.OpenHelp));
            }

            if (ContainsAny(lowered, "настройки", "settings", "ustawienia"))
            {
                return Task.FromResult(IntentParseResult.ForNavigation(UserIntentType.OpenSettings));
            }

            if (ContainsAny(lowered, "добавь др", "добавить день рождения", "add birthday", "dodaj urodziny"))
            {
                return Task.FromResult(IntentParseResult.ForNavigation(UserIntentType.OpenAddBirthday));
            }

            if (ContainsAny(lowered, "список", "покажи записи", "my entries", "list birthdays", "lista", "wpisy"))
            {
                return Task.FromResult(IntentParseResult.ForNavigation(UserIntentType.OpenList));
            }

            var removeMatch = RemoveRegex.Match(text);
            if (removeMatch.Success)
            {
                var name = removeMatch.Groups["name"].Value.Trim();
                if (!string.IsNullOrWhiteSpace(name))
                {
                    return Task.FromResult(IntentParseResult.ForRemove(name));
                }
            }

            var greetingMatch = GreetingPreviewRegex.Match(text);
            if (greetingMatch.Success)
            {
                var name = greetingMatch.Groups["name"].Value.Trim();
                var occasion = greetingMatch.Groups["occasion"].Value.Trim();
                if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(occasion))
                {
                    return Task.FromResult(IntentParseResult.ForGreetingPreview(name, occasion));
                }
            }

            if (TryParseSettingsIntent(user, text, lowered, out var settingsIntent))
            {
                return Task.FromResult(settingsIntent);
            }

            _metrics.TrackIntentFallback("no_match");
            return Task.FromResult(IntentParseResult.NoMatch);
        }
        catch
        {
            _metrics.TrackIntentParseError("exception");
            return Task.FromResult(IntentParseResult.NoMatch);
        }
        finally
        {
            _metrics.TrackIntentLatency(sw.Elapsed.TotalMilliseconds, "local");
        }
    }

    private bool TryParseSettingsIntent(User user, string text, string lowered, out IntentParseResult result)
    {
        var updated = new SettingsUpdate();
        var hasAny = false;

        if (DateHelpers.TryParseTimeHHmm(text, out var hh, out var mm))
        {
            updated = updated with { TimeHHmm = $"{hh:00}:{mm:00}" };
            hasAny = true;
        }

        if (ContainsAnyWord(lowered, "ru", "рус", "russian"))
        {
            updated = updated with { Lang = Language.Ru };
            hasAny = true;
        }
        else if (ContainsAnyWord(lowered, "pl", "polski", "polish"))
        {
            updated = updated with { Lang = Language.Pl };
            hasAny = true;
        }
        else if (ContainsAnyWord(lowered, "en", "english", "англ"))
        {
            updated = updated with { Lang = Language.En };
            hasAny = true;
        }

        if (ContainsAny(lowered, "formal", "формаль", "oficjal"))
        {
            updated = updated with { Tone = Tone.Formal };
            hasAny = true;
        }
        else if (ContainsAny(lowered, "friendly", "друж", "przyjaz"))
        {
            updated = updated with { Tone = Tone.Friendly };
            hasAny = true;
        }

        if (ContainsAny(lowered, "auto on", "авто вкл", "auto wł"))
        {
            updated = updated with { AutoGenerate = true };
            hasAny = true;
        }
        else if (ContainsAny(lowered, "auto off", "авто выкл", "auto wył"))
        {
            updated = updated with { AutoGenerate = false };
            hasAny = true;
        }

        if (_tzdb.Ids.Contains(text))
        {
            updated = updated with { Timezone = text };
            hasAny = true;
        }

        if (hasAny)
        {
            result = IntentParseResult.ForSettings(updated);
            return true;
        }

        result = IntentParseResult.NoMatch;
        return false;
    }

    private static bool ContainsAny(string text, params string[] words) =>
        words.Any(text.Contains);

    private static bool ContainsAnyWord(string text, params string[] words)
    {
        foreach (var word in words)
        {
            var pattern = $@"(^|[^a-zа-я0-9]){Regex.Escape(word)}([^a-zа-я0-9]|$)";
            if (Regex.IsMatch(text, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            {
                return true;
            }
        }

        return false;
    }
}

using BirthdayBot.Application.Interfaces;
using BirthdayBot.Domain.Entities;
using BirthdayBot.Domain.Enums;

namespace BirthdayBot.Infrastructure.Services;

/// <summary>
/// Deterministic, zero-cost fallback greeting generator.
/// OpenAI can improve these drafts, but the bot always remains functional without AI.
/// </summary>
public class GreetingGenerator : IGreetingGenerator
{
    private static readonly string[] RuFormal =
    [
        "{0}, поздравляю с днём рождения! Желаю крепкого здоровья, спокойствия, благополучия и новых успехов.",
        "С днём рождения, {0}! Желаю вдохновения, уверенности в своих силах и как можно больше хороших событий.",
        "{0}, примите мои тёплые поздравления с днём рождения! Пусть впереди будет много поводов для радости и гордости."
    ];

    private static readonly string[] RuFriendly =
    [
        "{0}, с днём рождения! Желаю побольше радости, энергии, классных событий и людей рядом, с которыми хорошо.",
        "С днём рождения, {0}! Пусть этот год принесёт много приятных сюрпризов, ярких впечатлений и поводов улыбаться.",
        "{0}, поздравляю! Желаю здоровья, лёгкости, вдохновения и побольше времени на то, что действительно радует."
    ];

    private static readonly string[] PlFormal =
    [
        "{0}, wszystkiego najlepszego z okazji urodzin! Życzę zdrowia, spokoju, pomyślności i wielu sukcesów.",
        "Wszystkiego najlepszego, {0}! Życzę inspiracji, pewności siebie i wielu dobrych chwil.",
        "{0}, proszę przyjąć serdeczne życzenia urodzinowe. Niech nadchodzący rok przyniesie wiele powodów do radości."
    ];

    private static readonly string[] PlFriendly =
    [
        "{0}, wszystkiego najlepszego! Dużo radości, energii, świetnych chwil i dobrych ludzi wokół.",
        "Sto lat, {0}! Niech ten rok przyniesie mnóstwo miłych niespodzianek i powodów do uśmiechu.",
        "{0}, wszystkiego najlepszego! Zdrowia, lekkości, inspiracji i dużo czasu na to, co naprawdę lubisz."
    ];

    private static readonly string[] EnFormal =
    [
        "Happy birthday, {0}! Wishing you good health, peace, prosperity, and continued success.",
        "Warm birthday wishes, {0}. May the year ahead bring inspiration, confidence, and many good moments.",
        "{0}, please accept my warmest birthday wishes. May the year ahead bring many reasons to celebrate."
    ];

    private static readonly string[] EnFriendly =
    [
        "Happy birthday, {0}! Wishing you lots of joy, energy, great moments, and good people around you.",
        "Happy birthday, {0}! Hope the year ahead brings plenty of happy surprises and reasons to smile.",
        "{0}, happy birthday! Wishing you health, inspiration, and more time for the things you truly enjoy."
    ];

    public string Generate(Language lang, Tone tone, string name, int? age)
    {
        var templates = (lang, tone) switch
        {
            (Language.Ru, Tone.Formal) => RuFormal,
            (Language.Ru, Tone.Friendly) => RuFriendly,
            (Language.Pl, Tone.Formal) => PlFormal,
            (Language.Pl, Tone.Friendly) => PlFriendly,
            (Language.En, Tone.Formal) => EnFormal,
            (Language.En, Tone.Friendly) => EnFriendly,
            _ => EnFriendly
        };

        var text = string.Format(templates[Random.Shared.Next(templates.Length)], name);

        // Mention age only when it is meaningful. Avoid forcing age into every greeting.
        if (age is { } knownAge && ShouldMentionAge(knownAge))
        {
            text += lang switch
            {
                Language.Ru => $" С юбилеем — {knownAge}!",
                Language.Pl => $" Wszystkiego najlepszego z okazji {knownAge}. urodzin!",
                _ => $" Happy {knownAge}th birthday!"
            };
        }

        return text;
    }

    public string GeneratePersonalized(User user, Birthday birthday, int? age)
    {
        var lang = birthday.GreetingLanguage ?? user.Lang;
        var parts = new List<string> { Generate(lang, user.Tone, birthday.FullName, age) };

        if (!string.IsNullOrWhiteSpace(birthday.Interests))
        {
            parts.Add(lang switch
            {
                Language.Ru => $"Пусть будет больше времени на то, что тебе нравится — {birthday.Interests}.",
                Language.Pl => $"Niech będzie więcej czasu na to, co lubisz — {birthday.Interests}.",
                _ => $"I hope you get more time for the things you enjoy — {birthday.Interests}."
            });
        }

        return string.Join(" ", parts);
    }

    private static bool ShouldMentionAge(int age) =>
        age is 18 or 21 || (age >= 30 && age % 10 == 0);
}

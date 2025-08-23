// path: backend/src/BirthdayBot.Infrastructure/Services/GreetingGenerator.cs
using BirthdayBot.Application.Interfaces;
using BirthdayBot.Domain.Enums;

namespace BirthdayBot.Infrastructure.Services;

public class GreetingGenerator : IGreetingGenerator
{
    private static readonly string[] RuFormal =
    [
        "Уважаемый(ая) {0}, примите мои наилучшие поздравления с днём рождения! Пусть {1}-й год принесёт успех и здоровье.",
        "{0}, поздравляю с днём рождения! Желаю стабильности, благополучия и гармонии на вашем {1}-м году.",
        "С днём рождения, {0}! Пусть новые достижения и крепкое здоровье сопровождают вас в {1}-й год жизни.",
        "{0}, искренне желаю сил, вдохновения и удачи. С {1}-м днём рождения!",
        "Поздравляю, {0}! Пусть каждый день {1}-го года будет продуктивным и радостным."
    ];

    private static readonly string[] RuFriendly =
    [
        "{0}, с днём варенья! Пусть {1}-й год принесёт огонь, драйв и классные приключения!",
        "Happy B-Day, {0}! На {1}-й год — побольше улыбок и кайфовых моментов!",
        "{0}, обнимаю! Пусть {1}-й год будет лёгким, тёплым и вкусным на впечатления!",
        "{0}, желаю здоровья, смеха и друзей рядом! С {1}-м днём рождения!",
        "{0}, пусть всё получается! На {1}-й год — смелости и ярких побед!"
    ];

    private static readonly string[] PlFormal =
    [
        "Szanowny/a {0}, najlepsze życzenia z okazji urodzin! Niech {1}. rok przyniesie sukces i zdrowie.",
        "{0}, gratulacje z okazji urodzin! Życzę stabilności, pomyślności i harmonii w {1}. roku.",
        "Wszystkiego najlepszego, {0}! Niech {1}. rok będzie pełen osiągnięć i dobrego zdrowia.",
        "{0}, życzę siły, inspiracji i szczęścia. Wszystkiego dobrego w {1}. roku!",
        "Gratulacje, {0}! Niech każdy dzień {1}. roku będzie owocny i radosny."
    ];

    private static readonly string[] PlFriendly =
    [
        "{0}, sto lat! Niech {1}. rok będzie mega pozytywny i pełen przygód!",
        "Happy B-Day, {0}! Na {1}. rok dużo uśmiechu i super chwil!",
        "{0}, wszystkiego najlepszego! Niech {1}. rok będzie lekki i ciepły!",
        "{0}, zdrowia, śmiechu i przyjaciół obok! Miłego {1}. roku!",
        "{0}, niech się udaje! Na {1}. rok odwagi i kolorowych zwycięstw!"
    ];

    private static readonly string[] EnFormal =
    [
        "Dear {0}, my warmest congratulations on your birthday! May your {1}th year bring success and good health.",
        "{0}, happy birthday! Wishing you stability, prosperity, and harmony in your {1}th year.",
        "Happy birthday, {0}! May your {1}th year be filled with achievements and strong health.",
        "{0}, wishing you strength, inspiration, and luck on your {1}th birthday!",
        "Congratulations, {0}! May every day of your {1}th year be productive and joyful."
    ];

    private static readonly string[] EnFriendly =
    [
        "{0}, happy b-day! May your {1}th year be full of fire, fun, and awesome adventures!",
        "Happy Birthday, {0}! Wishing tons of smiles and great vibes in your {1}th year!",
        "{0}, big hugs! Let your {1}th year be light, warm, and full of sweet moments!",
        "{0}, health, laughter, and friends around you — enjoy your {1}th year!",
        "{0}, you got this! Bold moves and bright wins in your {1}th year!"
    ];

    public string Generate(Language lang, Tone tone, string name, int age)
    {
        string[] set = (lang, tone) switch
        {
            (Language.Ru, Tone.Formal) => RuFormal,
            (Language.Ru, Tone.Friendly) => RuFriendly,
            (Language.Pl, Tone.Formal) => PlFormal,
            (Language.Pl, Tone.Friendly) => PlFriendly,
            (Language.En, Tone.Formal) => EnFormal,
            (Language.En, Tone.Friendly) => EnFriendly,
            _ => EnFriendly
        };
        var random = Random.Shared;
        var template = set[random.Next(set.Length)];
        return string.Format(template, name, age);
    }
}
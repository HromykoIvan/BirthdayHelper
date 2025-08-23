// path: backend/src/BirthdayBot.Infrastructure/Services/LocalizationService.cs
using BirthdayBot.Application.Interfaces;
using BirthdayBot.Domain.Enums;

namespace BirthdayBot.Infrastructure.Services;

public class LocalizationService : ILocalizationService
{
    private readonly Dictionary<(Language, string), string> _texts = new()
    {
        {(Language.Ru, "start"), "Привет! Я помогу помнить дни рождения. Команды: /add_birthday, /list, /remove, /settings, /help"},
        {(Language.Pl, "start"), "Cześć! Pomogę pamiętać o urodzinach. Komendy: /add_birthday, /list, /remove, /settings, /help"},
        {(Language.En, "start"), "Hi! I help you remember birthdays. Commands: /add_birthday, /list, /remove, /settings, /help"},

        {(Language.Ru, "ask_name"), "Введите имя именинника:"},
        {(Language.Pl, "ask_name"), "Podaj imię solenizanta:"},
        {(Language.En, "ask_name"), "Enter the person's name:"},

        {(Language.Ru, "ask_date"), "Введите дату рождения в формате YYYY-MM-DD:"},
        {(Language.Pl, "ask_date"), "Podaj datę urodzenia w formacie YYYY-MM-DD:"},
        {(Language.En, "ask_date"), "Enter date of birth (YYYY-MM-DD):"},

        {(Language.Ru, "ask_tz"), "Укажите таймзону (например, Europe/Warsaw). Enter для значения по умолчанию:"},
        {(Language.Pl, "ask_tz"), "Podaj strefę czasową (np. Europe/Warsaw). Enter aby użyć domyślnej:"},
        {(Language.En, "ask_tz"), "Provide timezone (e.g., Europe/Warsaw). Press Enter for default:"},

        {(Language.Ru, "saved"), "Сохранено ✅"},
        {(Language.Pl, "saved"), "Zapisano ✅"},
        {(Language.En, "saved"), "Saved ✅"},

        {(Language.Ru, "list_empty"), "Список пуст. Добавьте через /add_birthday"},
        {(Language.Pl, "list_empty"), "Lista pusta. Dodaj przez /add_birthday"},
        {(Language.En, "list_empty"), "List is empty. Add with /add_birthday"},

        {(Language.Ru, "removed"), "Удалено ✅"},
        {(Language.Pl, "removed"), "Usunięto ✅"},
        {(Language.En, "removed"), "Removed ✅"},

        {(Language.Ru, "settings_prompt"), "Настройки: отправьте время в формате HH:mm, язык (ru/pl/en), таймзону (Europe/Warsaw), auto on/off, tone formal/friendly."},
        {(Language.Pl, "settings_prompt"), "Ustawienia: wyślij HH:mm, język (ru/pl/en), strefę (Europe/Warsaw), auto on/off, ton formal/friendly."},
        {(Language.En, "settings_prompt"), "Settings: send HH:mm, language (ru/pl/en), timezone (Europe/Warsaw), auto on/off, tone formal/friendly."}
    };

    public string GetText(Language lang, string key)
    {
        if (_texts.TryGetValue((lang, key), out var value)) return value;
        return _texts[(Language.En, key)];
    }
}
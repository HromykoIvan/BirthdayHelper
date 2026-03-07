// path: backend/src/BirthdayBot.Infrastructure/Services/LocalizationService.cs
using BirthdayBot.Application.Interfaces;
using BirthdayBot.Domain.Enums;

namespace BirthdayBot.Infrastructure.Services;

public class LocalizationService : ILocalizationService
{
    private readonly Dictionary<(Language, string), string> _texts = new()
    {
        // Start / welcome (kept for fallback; main menu is now button-based)
        {(Language.Ru, "start"), "Привет! Я помогу помнить дни рождения.\nНажми кнопку ниже или используй команды: /add_birthday, /list, /remove, /settings, /help"},
        {(Language.Pl, "start"), "Cześć! Pomogę pamiętać o urodzinach.\nUżyj przycisków lub komend: /add_birthday, /list, /remove, /settings, /help"},
        {(Language.En, "start"), "Hi! I help you remember birthdays.\nUse the buttons below or commands: /add_birthday, /list, /remove, /settings, /help"},

        // Help
        {(Language.Ru, "help"),
            "<b>📖 Справка</b>\n\n" +
            "🎂 <b>Добавить ДР</b> — пошаговый мастер добавления\n" +
            "📋 <b>Мои записи</b> — список всех дней рождения по месяцам\n" +
            "⚙️ <b>Настройки</b> — время уведомлений, язык, тон\n\n" +
            "<i>Команды:</i> /add_birthday, /list, /remove, /settings"},
        {(Language.Pl, "help"),
            "<b>📖 Pomoc</b>\n\n" +
            "🎂 <b>Dodaj urodziny</b> — kreator krok po kroku\n" +
            "📋 <b>Moje wpisy</b> — lista urodzin wg miesięcy\n" +
            "⚙️ <b>Ustawienia</b> — czas powiadomień, język, ton\n\n" +
            "<i>Komendy:</i> /add_birthday, /list, /remove, /settings"},
        {(Language.En, "help"),
            "<b>📖 Help</b>\n\n" +
            "🎂 <b>Add Birthday</b> — step-by-step wizard\n" +
            "📋 <b>My Entries</b> — birthdays by month\n" +
            "⚙️ <b>Settings</b> — notification time, language, tone\n\n" +
            "<i>Commands:</i> /add_birthday, /list, /remove, /settings"},

        {(Language.Ru, "ask_name"), "Введите имя именинника:"},
        {(Language.Pl, "ask_name"), "Podaj imię solenizanta:"},
        {(Language.En, "ask_name"), "Enter the person's name:"},

        {(Language.Ru, "ask_date"), "Введите дату рождения в формате YYYY-MM-DD:"},
        {(Language.Pl, "ask_date"), "Podaj datę urodzenia w formacie YYYY-MM-DD:"},
        {(Language.En, "ask_date"), "Enter date of birth (YYYY-MM-DD):"},

        {(Language.Ru, "ask_tz"), "Укажите таймзону (например, Europe/Warsaw). Enter для значения по умолчанию:"},
        {(Language.Pl, "ask_tz"), "Podaj strefę czasową (np. Europe/Warsaw). Enter aby użyć domyślnej:"},
        {(Language.En, "ask_tz"), "Provide timezone (e.g., Europe/Warsaw). Press Enter for default:"},

        {(Language.Ru, "saved"), "✅ Сохранено"},
        {(Language.Pl, "saved"), "✅ Zapisano"},
        {(Language.En, "saved"), "✅ Saved"},

        {(Language.Ru, "list_empty"), "📋 Список пуст. Добавьте через кнопку «Добавить ДР»"},
        {(Language.Pl, "list_empty"), "📋 Lista pusta. Dodaj przez przycisk «Dodaj urodziny»"},
        {(Language.En, "list_empty"), "📋 List is empty. Add via the «Add Birthday» button"},

        {(Language.Ru, "removed"), "✅ Удалено"},
        {(Language.Pl, "removed"), "✅ Usunięto"},
        {(Language.En, "removed"), "✅ Removed"},

        {(Language.Ru, "settings_prompt"),
            "⚙️ <b>Настройки</b>\n\n" +
            "Отправь любую из следующих настроек текстом:\n" +
            "• Время уведомлений: <code>HH:mm</code>\n" +
            "• Язык: <code>ru</code> / <code>pl</code> / <code>en</code>\n" +
            "• Таймзона: <code>Europe/Warsaw</code>\n" +
            "• Авто-поздравления: <code>auto on</code> / <code>auto off</code>\n" +
            "• Тон: <code>formal</code> / <code>friendly</code>"},
        {(Language.Pl, "settings_prompt"),
            "⚙️ <b>Ustawienia</b>\n\n" +
            "Wyślij dowolne z poniższych ustawień tekstem:\n" +
            "• Czas powiadomień: <code>HH:mm</code>\n" +
            "• Język: <code>ru</code> / <code>pl</code> / <code>en</code>\n" +
            "• Strefa: <code>Europe/Warsaw</code>\n" +
            "• Auto: <code>auto on</code> / <code>auto off</code>\n" +
            "• Ton: <code>formal</code> / <code>friendly</code>"},
        {(Language.En, "settings_prompt"),
            "⚙️ <b>Settings</b>\n\n" +
            "Send any of these settings as text:\n" +
            "• Notification time: <code>HH:mm</code>\n" +
            "• Language: <code>ru</code> / <code>pl</code> / <code>en</code>\n" +
            "• Timezone: <code>Europe/Warsaw</code>\n" +
            "• Auto-greetings: <code>auto on</code> / <code>auto off</code>\n" +
            "• Tone: <code>formal</code> / <code>friendly</code>"},
    };

    public string GetText(Language lang, string key)
    {
        if (_texts.TryGetValue((lang, key), out var value)) return value;
        if (_texts.TryGetValue((Language.En, key), out var fallback)) return fallback;
        return key; // return key itself as last resort
    }
}

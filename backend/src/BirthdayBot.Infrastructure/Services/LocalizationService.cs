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

        // Welcome message
        {(Language.Ru, "welcome"), "👋 <b>Привет, {0}!</b>\n\nЯ помогу тебе не забыть ни одного дня рождения.\nВыбери действие:"},
        {(Language.Pl, "welcome"), "👋 <b>Cześć, {0}!</b>\n\nPomogę Ci nie zapomnieć o żadnych urodzinach.\nWybierz akcję:"},
        {(Language.En, "welcome"), "👋 <b>Hi, {0}!</b>\n\nI'll help you remember all birthdays.\nChoose an action:"},

        // Menu button labels
        {(Language.Ru, "menu_add"), "🎂 Добавить ДР"},
        {(Language.Pl, "menu_add"), "🎂 Dodaj urodziny"},
        {(Language.En, "menu_add"), "🎂 Add Birthday"},

        {(Language.Ru, "menu_list"), "📋 Мои записи"},
        {(Language.Pl, "menu_list"), "📋 Moje wpisy"},
        {(Language.En, "menu_list"), "📋 My Entries"},

        {(Language.Ru, "menu_settings"), "⚙️ Настройки"},
        {(Language.Pl, "menu_settings"), "⚙️ Ustawienia"},
        {(Language.En, "menu_settings"), "⚙️ Settings"},

        {(Language.Ru, "menu_help"), "❓ Помощь"},
        {(Language.Pl, "menu_help"), "❓ Pomoc"},
        {(Language.En, "menu_help"), "❓ Help"},

        {(Language.Ru, "back_to_menu"), "🏠 Главное меню"},
        {(Language.Pl, "back_to_menu"), "🏠 Menu główne"},
        {(Language.En, "back_to_menu"), "🏠 Main menu"},

        // Month names
        {(Language.Ru, "month_name_1"), "Январь"},
        {(Language.Ru, "month_name_2"), "Февраль"},
        {(Language.Ru, "month_name_3"), "Март"},
        {(Language.Ru, "month_name_4"), "Апрель"},
        {(Language.Ru, "month_name_5"), "Май"},
        {(Language.Ru, "month_name_6"), "Июнь"},
        {(Language.Ru, "month_name_7"), "Июль"},
        {(Language.Ru, "month_name_8"), "Август"},
        {(Language.Ru, "month_name_9"), "Сентябрь"},
        {(Language.Ru, "month_name_10"), "Октябрь"},
        {(Language.Ru, "month_name_11"), "Ноябрь"},
        {(Language.Ru, "month_name_12"), "Декабрь"},

        {(Language.Pl, "month_name_1"), "Styczeń"},
        {(Language.Pl, "month_name_2"), "Luty"},
        {(Language.Pl, "month_name_3"), "Marzec"},
        {(Language.Pl, "month_name_4"), "Kwiecień"},
        {(Language.Pl, "month_name_5"), "Maj"},
        {(Language.Pl, "month_name_6"), "Czerwiec"},
        {(Language.Pl, "month_name_7"), "Lipiec"},
        {(Language.Pl, "month_name_8"), "Sierpień"},
        {(Language.Pl, "month_name_9"), "Wrzesień"},
        {(Language.Pl, "month_name_10"), "Październik"},
        {(Language.Pl, "month_name_11"), "Listopad"},
        {(Language.Pl, "month_name_12"), "Grudzień"},

        {(Language.En, "month_name_1"), "January"},
        {(Language.En, "month_name_2"), "February"},
        {(Language.En, "month_name_3"), "March"},
        {(Language.En, "month_name_4"), "April"},
        {(Language.En, "month_name_5"), "May"},
        {(Language.En, "month_name_6"), "June"},
        {(Language.En, "month_name_7"), "July"},
        {(Language.En, "month_name_8"), "August"},
        {(Language.En, "month_name_9"), "September"},
        {(Language.En, "month_name_10"), "October"},
        {(Language.En, "month_name_11"), "November"},
        {(Language.En, "month_name_12"), "December"},

        // List view
        {(Language.Ru, "no_birthdays_this_month"), "В этом месяце дней рождения нет."},
        {(Language.Pl, "no_birthdays_this_month"), "W tym miesiącu nie ma urodzin."},
        {(Language.En, "no_birthdays_this_month"), "No birthdays this month."},

        {(Language.Ru, "total_entries"), "Всего записей: {0}"},
        {(Language.Pl, "total_entries"), "Wszystkich wpisów: {0}"},
        {(Language.En, "total_entries"), "Total entries: {0}"},

        {(Language.Ru, "all_entries"), "📋 <b>Все записи</b>"},
        {(Language.Pl, "all_entries"), "📋 <b>Wszystkie wpisy</b>"},
        {(Language.En, "all_entries"), "📋 <b>All Entries</b>"},

        {(Language.Ru, "upcoming_birthdays"), "🎉 <b>Ближайшие дни рождения</b>"},
        {(Language.Pl, "upcoming_birthdays"), "🎉 <b>Nadchodzące urodziny</b>"},
        {(Language.En, "upcoming_birthdays"), "🎉 <b>Upcoming Birthdays</b>"},

        // Language selection
        {(Language.Ru, "select_language"), "🌐 <b>Выбери язык интерфейса:</b>"},
        {(Language.Pl, "select_language"), "🌐 <b>Wybierz język interfejsu:</b>"},
        {(Language.En, "select_language"), "🌐 <b>Select interface language:</b>"},

        {(Language.Ru, "language_selected"), "✅ Язык изменён на {0}"},
        {(Language.Pl, "language_selected"), "✅ Język zmieniony na {0}"},
        {(Language.En, "language_selected"), "✅ Language changed to {0}"},

        {(Language.Ru, "lang_russian"), "Русский"},
        {(Language.Pl, "lang_russian"), "Rosyjski"},
        {(Language.En, "lang_russian"), "Russian"},

        {(Language.Ru, "lang_polish"), "Polski"},
        {(Language.Pl, "lang_polish"), "Polski"},
        {(Language.En, "lang_polish"), "Polish"},

        {(Language.Ru, "lang_english"), "English"},
        {(Language.Pl, "lang_english"), "Angielski"},
        {(Language.En, "lang_english"), "English"},

        // Settings
        {(Language.Ru, "settings_title"), "⚙️ <b>Настройки</b>"},
        {(Language.Pl, "settings_title"), "⚙️ <b>Ustawienia</b>"},
        {(Language.En, "settings_title"), "⚙️ <b>Settings</b>"},

        {(Language.Ru, "settings_notification_time"), "🕐 <b>Время уведомлений:</b> {0}"},
        {(Language.Pl, "settings_notification_time"), "🕐 <b>Czas powiadomień:</b> {0}"},
        {(Language.En, "settings_notification_time"), "🕐 <b>Notification time:</b> {0}"},

        {(Language.Ru, "settings_language"), "🌐 <b>Язык интерфейса:</b> {0}"},
        {(Language.Pl, "settings_language"), "🌐 <b>Język interfejsu:</b> {0}"},
        {(Language.En, "settings_language"), "🌐 <b>Interface language:</b> {0}"},

        {(Language.Ru, "settings_timezone"), "🌍 <b>Таймзона:</b> {0}"},
        {(Language.Pl, "settings_timezone"), "🌍 <b>Strefa czasowa:</b> {0}"},
        {(Language.En, "settings_timezone"), "🌍 <b>Timezone:</b> {0}"},

        {(Language.Ru, "settings_auto_greetings"), "🎉 <b>Авто-поздравления:</b> {0}"},
        {(Language.Pl, "settings_auto_greetings"), "🎉 <b>Auto-powinszowania:</b> {0}"},
        {(Language.En, "settings_auto_greetings"), "🎉 <b>Auto-greetings:</b> {0}"},

        {(Language.Ru, "settings_tone"), "💬 <b>Тон:</b> {0}"},
        {(Language.Pl, "settings_tone"), "💬 <b>Ton:</b> {0}"},
        {(Language.En, "settings_tone"), "💬 <b>Tone:</b> {0}"},

        {(Language.Ru, "settings_change"), "Изменить"},
        {(Language.Pl, "settings_change"), "Zmień"},
        {(Language.En, "settings_change"), "Change"},

        {(Language.Ru, "settings_on"), "Вкл"},
        {(Language.Pl, "settings_on"), "Wł"},
        {(Language.En, "settings_on"), "On"},

        {(Language.Ru, "settings_off"), "Выкл"},
        {(Language.Pl, "settings_off"), "Wył"},
        {(Language.En, "settings_off"), "Off"},

        {(Language.Ru, "settings_formal"), "Формальный"},
        {(Language.Pl, "settings_formal"), "Formalny"},
        {(Language.En, "settings_formal"), "Formal"},

        {(Language.Ru, "settings_friendly"), "Дружеский"},
        {(Language.Pl, "settings_friendly"), "Przyjazny"},
        {(Language.En, "settings_friendly"), "Friendly"},

        {(Language.Ru, "settings_select_time"), "🕐 Выбери время уведомлений:"},
        {(Language.Pl, "settings_select_time"), "🕐 Wybierz czas powiadomień:"},
        {(Language.En, "settings_select_time"), "🕐 Select notification time:"},

        {(Language.Ru, "settings_select_timezone"), "🌍 Введи таймзону (например, Europe/Warsaw):"},
        {(Language.Pl, "settings_select_timezone"), "🌍 Podaj strefę czasową (np. Europe/Warsaw):"},
        {(Language.En, "settings_select_timezone"), "🌍 Enter timezone (e.g., Europe/Warsaw):"},

        // Wizard prompts
        {(Language.Ru, "ask_lastname"), "Теперь введи <b>фамилию</b> (или нажми «Пропустить»)."},
        {(Language.Pl, "ask_lastname"), "Teraz podaj <b>nazwisko</b> (lub naciśnij «Pomiń»)."},
        {(Language.En, "ask_lastname"), "Now enter the <b>last name</b> (or press «Skip»)."},

        {(Language.Ru, "ask_relation"), "👥 <b>Кто этот человек для тебя?</b>\nВыбери кнопку или введи свой вариант."},
        {(Language.Pl, "ask_relation"), "👥 <b>Kim jest ta osoba dla Ciebie?</b>\nWybierz przycisk lub wpisz własną opcję."},
        {(Language.En, "ask_relation"), "👥 <b>Who is this person to you?</b>\nChoose a button or enter your own option."},

        {(Language.Ru, "ask_interests"), "💡 Расскажи про <b>интересы/хобби</b> именинника (для персональных поздравлений).\n\nНапример: <i>рыбалка, шахматы, кулинария</i>\nИли нажми «Пропустить»."},
        {(Language.Pl, "ask_interests"), "💡 Opowiedz o <b>zainteresowaniach/hobby</b> solenizanta (dla personalnych powinszowań).\n\nNa przykład: <i>wędkarstwo, szachy, gotowanie</i>\nLub naciśnij «Pomiń»."},
        {(Language.En, "ask_interests"), "💡 Tell me about the person's <b>interests/hobbies</b> (for personalized greetings).\n\nFor example: <i>fishing, chess, cooking</i>\nOr press «Skip»."},

        {(Language.Ru, "ask_greeting_lang"), "🌐 <b>На каком языке генерировать поздравления</b> для этого человека?\n(По умолчанию: язык интерфейса)"},
        {(Language.Pl, "ask_greeting_lang"), "🌐 <b>W jakim języku generować powinszowania</b> dla tej osoby?\n(Domyślnie: język interfejsu)"},
        {(Language.En, "ask_greeting_lang"), "🌐 <b>What language should greetings be generated in</b> for this person?\n(Default: interface language)"},

        {(Language.Ru, "confirm_summary"), "📋 <b>Проверим данные:</b>\n\n👤 <b>{0}</b>\n📅 {1}\n{2}{3}\n\nВсё верно?"},
        {(Language.Pl, "confirm_summary"), "📋 <b>Sprawdźmy dane:</b>\n\n👤 <b>{0}</b>\n📅 {1}\n{2}{3}\n\nWszystko w porządku?"},
        {(Language.En, "confirm_summary"), "📋 <b>Let's check the data:</b>\n\n👤 <b>{0}</b>\n📅 {1}\n{2}{3}\n\nEverything correct?"},

        // Calendar
        {(Language.Ru, "calendar_select_day"), "📅 Выбери день:"},
        {(Language.Pl, "calendar_select_day"), "📅 Wybierz dzień:"},
        {(Language.En, "calendar_select_day"), "📅 Select day:"},

        {(Language.Ru, "calendar_manual"), "⌨️ Ввести вручную"},
        {(Language.Pl, "calendar_manual"), "⌨️ Wpisz ręcznie"},
        {(Language.En, "calendar_manual"), "⌨️ Enter manually"},

        {(Language.Ru, "calendar_cancel"), "❌ Отмена"},
        {(Language.Pl, "calendar_cancel"), "❌ Anuluj"},
        {(Language.En, "calendar_cancel"), "❌ Cancel"},

        {(Language.Ru, "calendar_select_year"), "📅 Выбери год:"},
        {(Language.Pl, "calendar_select_year"), "📅 Wybierz rok:"},
        {(Language.En, "calendar_select_year"), "📅 Select year:"},

        // Today/Tomorrow
        {(Language.Ru, "today"), "СЕГОДНЯ"},
        {(Language.Pl, "today"), "DZIŚ"},
        {(Language.En, "today"), "TODAY"},

        {(Language.Ru, "tomorrow"), "ЗАВТРА"},
        {(Language.Pl, "tomorrow"), "JUTRO"},
        {(Language.En, "tomorrow"), "TOMORROW"},

        // Skip/Cancel buttons
        {(Language.Ru, "skip"), "➡️ Пропустить"},
        {(Language.Pl, "skip"), "➡️ Pomiń"},
        {(Language.En, "skip"), "➡️ Skip"},

        {(Language.Ru, "cancel"), "❌ Отмена"},
        {(Language.Pl, "cancel"), "❌ Anuluj"},
        {(Language.En, "cancel"), "❌ Cancel"},

        // Errors
        {(Language.Ru, "error_user_not_found"), "❌ Не удалось получить информацию о пользователе"},
        {(Language.Pl, "error_user_not_found"), "❌ Nie udało się uzyskać informacji o użytkowniku"},
        {(Language.En, "error_user_not_found"), "❌ Failed to get user information"},

        {(Language.Ru, "error_save_failed"), "❌ Ошибка сохранения"},
        {(Language.Pl, "error_save_failed"), "❌ Błąd zapisu"},
        {(Language.En, "error_save_failed"), "❌ Save error"},

        {(Language.Ru, "error_try_again"), "Произошла ошибка. Попробуйте ещё раз."},
        {(Language.Pl, "error_try_again"), "Wystąpił błąd. Spróbuj ponownie."},
        {(Language.En, "error_try_again"), "An error occurred. Please try again."},
    };

    public string GetText(Language lang, string key)
    {
        if (_texts.TryGetValue((lang, key), out var value)) return value;
        if (_texts.TryGetValue((Language.En, key), out var fallback)) return fallback;
        return key; // return key itself as last resort
    }
}

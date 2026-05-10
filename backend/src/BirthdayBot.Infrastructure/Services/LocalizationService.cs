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

        {(Language.Ru, "upcoming_empty_period"), "🎉 В выбранный период дней рождения нет."},
        {(Language.Pl, "upcoming_empty_period"), "🎉 Brak urodzin w wybranym okresie."},
        {(Language.En, "upcoming_empty_period"), "🎉 No birthdays in the selected period."},

        {(Language.Ru, "upcoming_today"), "📅 Сегодня"},
        {(Language.Pl, "upcoming_today"), "📅 Dziś"},
        {(Language.En, "upcoming_today"), "📅 Today"},

        {(Language.Ru, "upcoming_tomorrow"), "➡️ Завтра"},
        {(Language.Pl, "upcoming_tomorrow"), "➡️ Jutro"},
        {(Language.En, "upcoming_tomorrow"), "➡️ Tomorrow"},

        {(Language.Ru, "upcoming_7_days"), "🗓 7 дней"},
        {(Language.Pl, "upcoming_7_days"), "🗓 7 dni"},
        {(Language.En, "upcoming_7_days"), "🗓 7 days"},

        {(Language.Ru, "upcoming_this_month"), "📆 Этот месяц"},
        {(Language.Pl, "upcoming_this_month"), "📆 Ten miesiąc"},
        {(Language.En, "upcoming_this_month"), "📆 This month"},

        {(Language.Ru, "upcoming_next_month"), "📆 След. месяц"},
        {(Language.Pl, "upcoming_next_month"), "📆 Nast. miesiąc"},
        {(Language.En, "upcoming_next_month"), "📆 Next month"},

        {(Language.Ru, "all_records_button"), "📑 Все записи"},
        {(Language.Pl, "all_records_button"), "📑 Wszystkie wpisy"},
        {(Language.En, "all_records_button"), "📑 All entries"},

        {(Language.Ru, "showing_entries"), "Показаны записи {0}-{1} из {2}"},
        {(Language.Pl, "showing_entries"), "Pokazano wpisy {0}-{1} z {2}"},
        {(Language.En, "showing_entries"), "Showing entries {0}-{1} of {2}"},

        {(Language.Ru, "prev_page"), "◀️ Назад"},
        {(Language.Pl, "prev_page"), "◀️ Wstecz"},
        {(Language.En, "prev_page"), "◀️ Previous"},

        {(Language.Ru, "next_page"), "Вперёд ▶️"},
        {(Language.Pl, "next_page"), "Dalej ▶️"},
        {(Language.En, "next_page"), "Next ▶️"},

        {(Language.Ru, "delete_entry"), "🗑 Удалить запись"},
        {(Language.Pl, "delete_entry"), "🗑 Usuń wpis"},
        {(Language.En, "delete_entry"), "🗑 Delete entry"},

        {(Language.Ru, "delete_list_title"), "🗑 <b>Удаление записи</b>"},
        {(Language.Pl, "delete_list_title"), "🗑 <b>Usuwanie wpisu</b>"},
        {(Language.En, "delete_list_title"), "🗑 <b>Delete Entry</b>"},

        {(Language.Ru, "delete_select_instruction"), "Выберите номер записи для удаления:"},
        {(Language.Pl, "delete_select_instruction"), "Wybierz numer wpisu do usunięcia:"},
        {(Language.En, "delete_select_instruction"), "Choose the entry number to delete:"},

        {(Language.Ru, "delete_confirm"), "Удалить запись <b>{0}</b> ({1})?"},
        {(Language.Pl, "delete_confirm"), "Usunąć wpis <b>{0}</b> ({1})?"},
        {(Language.En, "delete_confirm"), "Delete <b>{0}</b> ({1})?"},

        {(Language.Ru, "delete_confirm_button"), "✅ Удалить"},
        {(Language.Pl, "delete_confirm_button"), "✅ Usuń"},
        {(Language.En, "delete_confirm_button"), "✅ Delete"},

        {(Language.Ru, "delete_cancel_button"), "❌ Отмена"},
        {(Language.Pl, "delete_cancel_button"), "❌ Anuluj"},
        {(Language.En, "delete_cancel_button"), "❌ Cancel"},

        {(Language.Ru, "entry_not_found"), "Запись не найдена."},
        {(Language.Pl, "entry_not_found"), "Nie znaleziono wpisu."},
        {(Language.En, "entry_not_found"), "Entry not found."},

        {(Language.Ru, "all_entries_empty"), "📋 Список пуст. Добавьте запись через кнопку ниже."},
        {(Language.Pl, "all_entries_empty"), "📋 Lista jest pusta. Dodaj wpis przyciskiem poniżej."},
        {(Language.En, "all_entries_empty"), "📋 The list is empty. Add an entry using the button below."},

        {(Language.Ru, "next_occurrence"), "след. {0}, {1} {2}"},
        {(Language.Pl, "next_occurrence"), "nast. {0}, {1} {2}"},
        {(Language.En, "next_occurrence"), "next {0}, {1} {2}"},

        {(Language.Ru, "years_word"), "{0}"},
        {(Language.Pl, "years_word"), "lat"},
        {(Language.En, "years_word"), "years"},

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

        {(Language.Ru, "wizard_start"),
            "🎂 <b>Добавляем день рождения</b>\n\n" +
            "① <b>Имя</b> → ② Фамилия → ③ Дата → ④ Кто это → ⑤ Интересы → ⑥ Подтверждение\n\n" +
            "Введи <b>имя</b> именинника (например: <code>Маша</code>)."},
        {(Language.Pl, "wizard_start"),
            "🎂 <b>Dodajemy urodziny</b>\n\n" +
            "① <b>Imię</b> → ② Nazwisko → ③ Data → ④ Relacja → ⑤ Zainteresowania → ⑥ Potwierdzenie\n\n" +
            "Podaj <b>imię</b> solenizanta (np. <code>Maria</code>)."},
        {(Language.En, "wizard_start"),
            "🎂 <b>Adding a birthday</b>\n\n" +
            "① <b>Name</b> → ② Last name → ③ Date → ④ Relation → ⑤ Interests → ⑥ Confirmation\n\n" +
            "Enter the person's <b>name</b> (for example: <code>Mary</code>)."},

        {(Language.Ru, "wizard_enter_name"), "Введи <b>имя</b> (например: <code>Маша</code>)."},
        {(Language.Pl, "wizard_enter_name"), "Podaj <b>imię</b> (np. <code>Maria</code>)."},
        {(Language.En, "wizard_enter_name"), "Enter the <b>name</b> (for example: <code>Mary</code>)."},

        {(Language.Ru, "wizard_name_label"), "Имя:"},
        {(Language.Pl, "wizard_name_label"), "Imię:"},
        {(Language.En, "wizard_name_label"), "Name:"},

        {(Language.Ru, "wizard_name_saved"), "👤 Имя: <b>{0}</b>\n\n{1}"},
        {(Language.Pl, "wizard_name_saved"), "👤 Imię: <b>{0}</b>\n\n{1}"},
        {(Language.En, "wizard_name_saved"), "👤 Name: <b>{0}</b>\n\n{1}"},

        {(Language.Ru, "wizard_name_length_error"), "Имя должно быть 2–64 символа. Попробуй ещё раз."},
        {(Language.Pl, "wizard_name_length_error"), "Imię musi mieć 2–64 znaki. Spróbuj ponownie."},
        {(Language.En, "wizard_name_length_error"), "Name must be 2–64 characters. Please try again."},

        {(Language.Ru, "wizard_lastname_length_error"), "Фамилия слишком длинная (макс. 64 символа)."},
        {(Language.Pl, "wizard_lastname_length_error"), "Nazwisko jest za długie (maks. 64 znaki)."},
        {(Language.En, "wizard_lastname_length_error"), "Last name is too long (max. 64 characters)."},

        {(Language.Ru, "wizard_date_prompt"), "📅 Выбери <b>дату рождения</b> в календаре или введи вручную (<code>ДД.ММ.ГГГГ</code>)."},
        {(Language.Pl, "wizard_date_prompt"), "📅 Wybierz <b>datę urodzenia</b> w kalendarzu albo wpisz ją ręcznie (<code>DD.MM.RRRR</code>)."},
        {(Language.En, "wizard_date_prompt"), "📅 Select the <b>birthday date</b> in the calendar or enter it manually (<code>DD.MM.YYYY</code>)."},

        {(Language.Ru, "wizard_edit_date_prompt"), "Введи <b>дату</b> или выбери в календаре."},
        {(Language.Pl, "wizard_edit_date_prompt"), "Podaj <b>datę</b> albo wybierz ją w kalendarzu."},
        {(Language.En, "wizard_edit_date_prompt"), "Enter the <b>date</b> or select it in the calendar."},

        {(Language.Ru, "wizard_date_parse_error"), "Не понял дату. Введи <code>ДД.ММ</code> или <code>ДД.ММ.ГГГГ</code>, или выбери день в календаре выше."},
        {(Language.Pl, "wizard_date_parse_error"), "Nie rozumiem daty. Wpisz <code>DD.MM</code> lub <code>DD.MM.RRRR</code> albo wybierz dzień w kalendarzu powyżej."},
        {(Language.En, "wizard_date_parse_error"), "I could not parse the date. Enter <code>DD.MM</code> or <code>DD.MM.YYYY</code>, or select a day in the calendar above."},

        {(Language.Ru, "wizard_selected_date"), "📅 Выбрана дата: <b>{0}</b>"},
        {(Language.Pl, "wizard_selected_date"), "📅 Wybrana data: <b>{0}</b>"},
        {(Language.En, "wizard_selected_date"), "📅 Selected date: <b>{0}</b>"},

        {(Language.Ru, "wizard_manual_date"), "⌨️ Введи дату вручную: <code>ДД.ММ.ГГГГ</code>"},
        {(Language.Pl, "wizard_manual_date"), "⌨️ Wpisz datę ręcznie: <code>DD.MM.RRRR</code>"},
        {(Language.En, "wizard_manual_date"), "⌨️ Enter the date manually: <code>DD.MM.YYYY</code>"},

        {(Language.Ru, "wizard_saved_birthday"), "✅ Сохранено!\n\n🎂 <b>{0}</b>, {1}"},
        {(Language.Pl, "wizard_saved_birthday"), "✅ Zapisano!\n\n🎂 <b>{0}</b>, {1}"},
        {(Language.En, "wizard_saved_birthday"), "✅ Saved!\n\n🎂 <b>{0}</b>, {1}"},

        {(Language.Ru, "wizard_next_action"), "Что дальше?"},
        {(Language.Pl, "wizard_next_action"), "Co dalej?"},
        {(Language.En, "wizard_next_action"), "What's next?"},

        {(Language.Ru, "wizard_cancelled"), "❌ Отменено"},
        {(Language.Pl, "wizard_cancelled"), "❌ Anulowano"},
        {(Language.En, "wizard_cancelled"), "❌ Cancelled"},

        {(Language.Ru, "wizard_save_question"), "Сохранить?"},
        {(Language.Pl, "wizard_save_question"), "Zapisać?"},
        {(Language.En, "wizard_save_question"), "Save?"},

        {(Language.Ru, "confirm_save"), "✅ Сохранить"},
        {(Language.Pl, "confirm_save"), "✅ Zapisz"},
        {(Language.En, "confirm_save"), "✅ Save"},

        {(Language.Ru, "edit_name"), "✏️ Имя"},
        {(Language.Pl, "edit_name"), "✏️ Imię"},
        {(Language.En, "edit_name"), "✏️ Name"},

        {(Language.Ru, "edit_date"), "📅 Дата"},
        {(Language.Pl, "edit_date"), "📅 Data"},
        {(Language.En, "edit_date"), "📅 Date"},

        {(Language.Ru, "relation_family"), "👪 Семья"},
        {(Language.Pl, "relation_family"), "👪 Rodzina"},
        {(Language.En, "relation_family"), "👪 Family"},

        {(Language.Ru, "relation_partner"), "❤️ Партнёр"},
        {(Language.Pl, "relation_partner"), "❤️ Partner"},
        {(Language.En, "relation_partner"), "❤️ Partner"},

        {(Language.Ru, "relation_friend"), "🎓 Друг"},
        {(Language.Pl, "relation_friend"), "🎓 Przyjaciel"},
        {(Language.En, "relation_friend"), "🎓 Friend"},

        {(Language.Ru, "relation_colleague"), "💼 Коллега"},
        {(Language.Pl, "relation_colleague"), "💼 Współpracownik"},
        {(Language.En, "relation_colleague"), "💼 Colleague"},

        {(Language.Ru, "relation_other"), "Другое"},
        {(Language.Pl, "relation_other"), "Inne"},
        {(Language.En, "relation_other"), "Other"},

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

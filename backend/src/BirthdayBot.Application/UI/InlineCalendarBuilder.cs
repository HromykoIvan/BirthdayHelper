using System.Globalization;
using Telegram.Bot.Types.ReplyMarkups;

namespace BirthdayBot.Application.UI;

/// <summary>
/// Builds inline-keyboard calendars for date picking and month navigation.
/// Callback data format:
///   cal:day:YYYY-MM-DD       — user selected a day
///   cal:prev:YYYY-MM         — navigate to previous month
///   cal:next:YYYY-MM         — navigate to next month
///   cal:pickyear:YYYY-MM     — open year picker (click on month/year header)
///   cal:decade:START-END:MM  — show years in decade (e.g., cal:decade:2020-2029:03)
///   cal:year:YYYY-MM         — select year and return to calendar
///   cal:ignore               — non-clickable header cell
///   cal:manual               — user wants to type the date manually
///   cal:cancel               — cancel date selection
/// </summary>
public static class InlineCalendarBuilder
{
    private static readonly string[] RuMonths =
    {
        "", "Январь", "Февраль", "Март", "Апрель", "Май", "Июнь",
        "Июль", "Август", "Сентябрь", "Октябрь", "Ноябрь", "Декабрь"
    };

    private static readonly string[] DayHeaders = { "Пн", "Вт", "Ср", "Чт", "Пт", "Сб", "Вс" };

    /// <summary>
    /// Builds a month-grid calendar keyboard.
    /// </summary>
    public static InlineKeyboardMarkup BuildMonthGrid(int year, int month, DateOnly? highlightDate = null)
    {
        var rows = new List<InlineKeyboardButton[]>();

        // Row 1: navigation — [◀️] [Month Year] [▶️]
        var prev = month == 1 ? $"{year - 1}-12" : $"{year}-{month - 1:D2}";
        var next = month == 12 ? $"{year + 1}-01" : $"{year}-{month + 1:D2}";

        rows.Add(new[]
        {
            InlineKeyboardButton.WithCallbackData("◀️", $"cal:prev:{prev}"),
            InlineKeyboardButton.WithCallbackData($"{RuMonths[month]} {year}", $"cal:pickyear:{year}-{month:D2}"),
            InlineKeyboardButton.WithCallbackData("▶️", $"cal:next:{next}"),
        });

        // Row 2: day-of-week headers (non-clickable)
        rows.Add(DayHeaders.Select(d =>
            InlineKeyboardButton.WithCallbackData(d, "cal:ignore")).ToArray());

        // Rows 3+: day numbers
        var firstDay = new DateTime(year, month, 1);
        var daysInMonth = DateTime.DaysInMonth(year, month);
        // Monday=0 .. Sunday=6  (ISO)
        var startOffset = ((int)firstDay.DayOfWeek + 6) % 7;

        var week = new InlineKeyboardButton[7];
        for (var i = 0; i < 7; i++)
            week[i] = InlineKeyboardButton.WithCallbackData(" ", "cal:ignore");

        for (var day = 1; day <= daysInMonth; day++)
        {
            var col = (startOffset + day - 1) % 7;
            if (col == 0 && day > 1)
            {
                rows.Add(week);
                week = new InlineKeyboardButton[7];
                for (var i = 0; i < 7; i++)
                    week[i] = InlineKeyboardButton.WithCallbackData(" ", "cal:ignore");
            }

            var dateStr = $"{year}-{month:D2}-{day:D2}";
            var label = day.ToString();

            // Highlight today or a selected date
            if (highlightDate.HasValue &&
                highlightDate.Value.Year == year &&
                highlightDate.Value.Month == month &&
                highlightDate.Value.Day == day)
            {
                label = $"[{day}]";
            }

            week[col] = InlineKeyboardButton.WithCallbackData(label, $"cal:day:{dateStr}");
        }

        rows.Add(week); // last partial week

        // Bottom row: manual entry + cancel
        rows.Add(new[]
        {
            InlineKeyboardButton.WithCallbackData("⌨️ Ввести вручную", "cal:manual"),
            InlineKeyboardButton.WithCallbackData("❌ Отмена", "cal:cancel"),
        });

        return new InlineKeyboardMarkup(rows);
    }

    /// <summary>
    /// Builds a month navigation bar for the birthday list viewer.
    /// Shows [◀️ Prev] [Current Month] [Next ▶️] plus action buttons.
    /// </summary>
    public static InlineKeyboardMarkup BuildMonthNavigator(int year, int month, bool hasEntries)
    {
        var rows = new List<InlineKeyboardButton[]>();

        var prevMonth = month == 1 ? 12 : month - 1;
        var prevYear = month == 1 ? year - 1 : year;
        var nextMonth = month == 12 ? 1 : month + 1;
        var nextYear = month == 12 ? year + 1 : year;

        // Month navigation row
        rows.Add(new[]
        {
            InlineKeyboardButton.WithCallbackData(
                $"◀️ {RuMonths[prevMonth][..3]}",
                $"list:month:{prevYear}-{prevMonth:D2}"),
            InlineKeyboardButton.WithCallbackData(
                $"📅 {RuMonths[month]} {year}",
                "cal:ignore"),
            InlineKeyboardButton.WithCallbackData(
                $"{RuMonths[nextMonth][..3]} ▶️",
                $"list:month:{nextYear}-{nextMonth:D2}"),
        });

        // Action buttons
        rows.Add(new[]
        {
            InlineKeyboardButton.WithCallbackData("📑 Все записи", "list:all"),
            InlineKeyboardButton.WithCallbackData("🏠 Меню", "menu:home"),
        });

        return new InlineKeyboardMarkup(rows);
    }

    /// <summary>
    /// Gets the Russian month name (1-based index).
    /// </summary>
    public static string GetMonthName(int month) => RuMonths[month];

    /// <summary>
    /// Builds a year picker showing decades (1900-1999, 2000-2009, etc.).
    /// When a decade is clicked, shows individual years in that range.
    /// </summary>
    public static InlineKeyboardMarkup BuildYearPicker(int currentYear, int currentMonth)
    {
        var rows = new List<InlineKeyboardButton[]>();
        var currentDecade = (currentYear / 10) * 10;
        var startDecade = 1900;
        var endDecade = ((DateTime.UtcNow.Year + 1) / 10) * 10;

        // Group decades into rows of 2
        for (var decade = endDecade; decade >= startDecade; decade -= 10)
        {
            var decadeEnd = decade + 9;
            var label = decade == currentDecade
                ? $"▶️ {decade}-{decadeEnd}"
                : $"{decade}-{decadeEnd}";

            if ((endDecade - decade) % 20 == 0)
            {
                // Start new row
                rows.Add(new[]
                {
                    InlineKeyboardButton.WithCallbackData(
                        label,
                        $"cal:decade:{decade}-{decadeEnd}:{currentMonth:D2}")
                });
            }
            else
            {
                // Add to last row
                var lastRow = rows[^1].ToList();
                lastRow.Add(InlineKeyboardButton.WithCallbackData(
                    label,
                    $"cal:decade:{decade}-{decadeEnd}:{currentMonth:D2}"));
                rows[^1] = lastRow.ToArray();
            }
        }

        // Back button
        rows.Add(new[]
        {
            InlineKeyboardButton.WithCallbackData("◀️ Назад", $"cal:year:{currentYear}-{currentMonth:D2}")
        });

        return new InlineKeyboardMarkup(rows);
    }

    /// <summary>
    /// Builds a year list within a specific decade (e.g., 2020-2029).
    /// </summary>
    public static InlineKeyboardMarkup BuildDecadeYears(int decadeStart, int decadeEnd, int currentMonth)
    {
        var rows = new List<InlineKeyboardButton[]>();
        var currentYear = DateTime.UtcNow.Year;

        // Group years into rows of 3
        for (var year = decadeStart; year <= decadeEnd; year++)
        {
            var label = year == currentYear ? $"▶️ {year}" : year.ToString();

            if ((year - decadeStart) % 3 == 0)
            {
                // Start new row
                rows.Add(new[]
                {
                    InlineKeyboardButton.WithCallbackData(
                        label,
                        $"cal:year:{year}-{currentMonth:D2}")
                });
            }
            else
            {
                // Add to last row
                var lastRow = rows[^1].ToList();
                lastRow.Add(InlineKeyboardButton.WithCallbackData(
                    label,
                    $"cal:year:{year}-{currentMonth:D2}"));
                rows[^1] = lastRow.ToArray();
            }
        }

        // Back button to return to decades
        rows.Add(new[]
        {
            InlineKeyboardButton.WithCallbackData("◀️ Назад", $"cal:pickyear:{decadeStart}-{currentMonth:D2}")
        });

        return new InlineKeyboardMarkup(rows);
    }
}

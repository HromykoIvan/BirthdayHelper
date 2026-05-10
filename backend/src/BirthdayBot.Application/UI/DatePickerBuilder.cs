using BirthdayBot.Domain.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace BirthdayBot.Application.UI;

/// <summary>
/// Builds inline keyboards for the 3-step date picker:
///   dp:d:N       — day N selected
///   dp:m:N       — month N selected
///   dp:y:YYYY    — year YYYY selected
///   dp:noyear    — year unknown / skip
///   dp:yp:N      — switch to year page N
///   dp:back:d    — go back to day picker
///   dp:back:m    — go back to month picker
///   dp:cancel    — cancel the whole date picker
/// </summary>
public static class DatePickerBuilder
{
    // ─── Day picker (1-31, 7 per row) ────────────────────────────────────────

    public static InlineKeyboardMarkup DayGrid(Language lang)
    {
        var rows = new List<InlineKeyboardButton[]>();

        var week = new List<InlineKeyboardButton>();
        for (var d = 1; d <= 31; d++)
        {
            week.Add(InlineKeyboardButton.WithCallbackData(d.ToString(), $"dp:d:{d}"));
            if (week.Count == 7 || d == 31)
            {
                rows.Add(week.ToArray());
                week = new List<InlineKeyboardButton>();
            }
        }

        rows.Add(new[] { CancelBtn(lang) });
        return new InlineKeyboardMarkup(rows);
    }

    // ─── Month picker (3 per row) ─────────────────────────────────────────────

    public static InlineKeyboardMarkup MonthGrid(Language lang)
    {
        var names = MonthNames(lang);
        var rows = new List<InlineKeyboardButton[]>();

        for (var m = 1; m <= 12; m += 3)
        {
            var row = new List<InlineKeyboardButton>();
            for (var i = 0; i < 3 && m + i <= 12; i++)
            {
                var idx = m + i;
                row.Add(InlineKeyboardButton.WithCallbackData(names[idx], $"dp:m:{idx}"));
            }
            rows.Add(row.ToArray());
        }

        rows.Add(new[]
        {
            InlineKeyboardButton.WithCallbackData(BackLabel(lang), "dp:back:d"),
            CancelBtn(lang)
        });
        return new InlineKeyboardMarkup(rows);
    }

    // ─── Year picker (page-based, 5 per row, 25 per page) ────────────────────

    public static InlineKeyboardMarkup YearGrid(Language lang, int page = 0)
    {
        var currentYear = DateTime.UtcNow.Year;
        // page 0 = currentYear..currentYear-24
        // page 1 = currentYear-25..currentYear-49
        // page 2 = currentYear-50..currentYear-74
        var startYear = currentYear - page * 25;
        var endYear   = Math.Max(startYear - 24, 1900);

        var rows = new List<InlineKeyboardButton[]>();

        var row = new List<InlineKeyboardButton>();
        for (var y = startYear; y >= endYear; y--)
        {
            row.Add(InlineKeyboardButton.WithCallbackData(y.ToString(), $"dp:y:{y}"));
            if (row.Count == 5)
            {
                rows.Add(row.ToArray());
                row = new List<InlineKeyboardButton>();
            }
        }
        if (row.Count > 0) rows.Add(row.ToArray());

        // Navigation + actions row
        var navRow = new List<InlineKeyboardButton>();

        if (page > 0)
            navRow.Add(InlineKeyboardButton.WithCallbackData(NewerLabel(lang), $"dp:yp:{page - 1}"));

        navRow.Add(InlineKeyboardButton.WithCallbackData(SkipYearLabel(lang), "dp:noyear"));

        if (endYear > 1900)
            navRow.Add(InlineKeyboardButton.WithCallbackData(OlderLabel(lang), $"dp:yp:{page + 1}"));

        rows.Add(navRow.ToArray());

        rows.Add(new[]
        {
            InlineKeyboardButton.WithCallbackData(BackLabel(lang), "dp:back:m"),
            CancelBtn(lang)
        });

        return new InlineKeyboardMarkup(rows);
    }

    // ─── Static text helpers ─────────────────────────────────────────────────

    /// <summary>Formats the date-form header shown in the editable message.</summary>
    public static string FormHeader(Language lang, int? day, int? month, bool showYear = false) =>
        lang switch
        {
            Language.Pl => $"📅 <b>Data urodzin</b>\n\nDzień: <b>{day?.ToString() ?? "—"}</b>\nMiesiąc: <b>{(month.HasValue ? MonthNames(lang)[month.Value] : "—")}</b>" +
                           (showYear ? $"\nRok: <b>{"—"}</b> <i>(opcjonalnie)</i>" : ""),
            Language.En => $"📅 <b>Birthday date</b>\n\nDay: <b>{day?.ToString() ?? "—"}</b>\nMonth: <b>{(month.HasValue ? MonthNames(lang)[month.Value] : "—")}</b>" +
                           (showYear ? $"\nYear: <b>{"—"}</b> <i>(optional)</i>" : ""),
            _            => $"📅 <b>Дата рождения</b>\n\nЧисло: <b>{day?.ToString() ?? "—"}</b>\nМесяц: <b>{(month.HasValue ? MonthNames(lang)[month.Value] : "—")}</b>" +
                           (showYear ? $"\nГод: <b>{"—"}</b> <i>(необязательно)</i>" : ""),
        };

    public static string[] MonthNames(Language lang) => lang switch
    {
        Language.Pl => new[] { "", "Styczeń", "Luty", "Marzec", "Kwiecień", "Maj", "Czerwiec",
                                "Lipiec", "Sierpień", "Wrzesień", "Październik", "Listopad", "Grudzień" },
        Language.En => new[] { "", "January", "February", "March", "April", "May", "June",
                                "July", "August", "September", "October", "November", "December" },
        _            => new[] { "", "Январь", "Февраль", "Март", "Апрель", "Май", "Июнь",
                                "Июль", "Август", "Сентябрь", "Октябрь", "Ноябрь", "Декабрь" }
    };

    // ─── Button labels ────────────────────────────────────────────────────────

    private static InlineKeyboardButton CancelBtn(Language lang) =>
        InlineKeyboardButton.WithCallbackData(
            lang switch { Language.Pl => "❌ Anuluj", Language.En => "❌ Cancel", _ => "❌ Отмена" },
            "dp:cancel");

    private static string BackLabel(Language lang) => lang switch
    {
        Language.Pl => "◀️ Wstecz",
        Language.En => "◀️ Back",
        _            => "◀️ Назад"
    };

    private static string SkipYearLabel(Language lang) => lang switch
    {
        Language.Pl => "⏭ Rok nieznany",
        Language.En => "⏭ Year unknown",
        _            => "⏭ Год неизвестен"
    };

    private static string NewerLabel(Language lang) => lang switch
    {
        Language.Pl => "▶️ Nowsze",
        Language.En => "▶️ Newer",
        _            => "▶️ Новее"
    };

    private static string OlderLabel(Language lang) => lang switch
    {
        Language.Pl => "◀️ Старше",
        Language.En => "◀️ Older",
        _            => "◀️ Старше"
    };
}

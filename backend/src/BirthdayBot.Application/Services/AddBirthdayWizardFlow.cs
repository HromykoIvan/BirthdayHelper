using System;
using System.Text.RegularExpressions;
using BirthdayBot.Application.Interfaces;
using BirthdayBot.Application.Models;
using BirthdayBot.Application.UI;
using BirthdayBot.Application.Utils;
using BirthdayBot.Domain.Entities;
using BirthdayBot.Domain.Enums;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace BirthdayBot.Application.Services;

/// <summary>
/// Multi-step wizard for adding a birthday:
/// Name → LastName → Date (day/month/year pickers) → Relation → Interests → Confirm.
/// </summary>
public sealed class AddBirthdayWizardFlow : IWizardFlow
{
    private readonly ITelegramBotClient _bot;
    private readonly IConversationSessionStore _store;
    private readonly IBirthdayRepository _birthdays;
    private readonly IUserRepository _users;
    private readonly ILocalizationService _i18n;
    private readonly ILogger<AddBirthdayWizardFlow> _logger;

    private static readonly Regex DateRegex =
        new(@"^(?<d>\d{1,2})[.\-/](?<m>\d{1,2})(?:[.\-/](?<y>\d{4}))?$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private ReplyKeyboardMarkup NameKb(Language lang) => new(new[]
    {
        new KeyboardButton[] { _i18n.GetText(lang, "cancel") }
    })
    { ResizeKeyboard = true, OneTimeKeyboard = true };

    private InlineKeyboardMarkup ConfirmKb(Language lang) => new(new[]
    {
        new [] { InlineKeyboardButton.WithCallbackData(_i18n.GetText(lang, "confirm_save"), "add:save") },
        new [] { InlineKeyboardButton.WithCallbackData(_i18n.GetText(lang, "edit_name"), "add:editname"),
                 InlineKeyboardButton.WithCallbackData(_i18n.GetText(lang, "edit_date"), "add:editdate") },
        new [] { InlineKeyboardButton.WithCallbackData(_i18n.GetText(lang, "cancel"), "add:cancel") }
    });

    public AddBirthdayWizardFlow(
        ITelegramBotClient bot,
        IConversationSessionStore store,
        IBirthdayRepository birthdays,
        IUserRepository users,
        ILocalizationService i18n,
        ILogger<AddBirthdayWizardFlow> logger)
    {
        _bot = bot;
        _store = store;
        _birthdays = birthdays;
        _users = users;
        _i18n = i18n;
        _logger = logger;
    }

    public async Task<bool> TryHandleAsync(Update update, CancellationToken ct = default)
    {
        try
        {
            var chatId = update.Message?.Chat.Id
                         ?? update.CallbackQuery?.Message?.Chat.Id
                         ?? 0;
            var userId = update.Message?.From?.Id
                         ?? update.CallbackQuery?.From.Id
                         ?? 0;

            if (chatId == 0 || userId == 0)
                return false;

            var text = update.Message?.Text;

            // ── Start wizard via /add_birthday ──
            if (text is "/add_birthday")
            {
                var lang = await ResolveLanguageAsync(userId, ct);
                var session = new AddBirthdayWizardSession(chatId, userId);
                _store.Upsert(session);

                await _bot.SendTextMessageAsync(
                    chatId,
                    _i18n.GetText(lang, "wizard_start"),
                    parseMode: ParseMode.Html,
                    replyMarkup: NameKb(lang),
                    cancellationToken: ct);

                return true;
            }

            // ── Date picker callbacks (dp:*) ──
            if (update.CallbackQuery?.Data is { } dpData && dpData.StartsWith("dp:", StringComparison.Ordinal))
            {
                if (!_store.TryGet(chatId, out var dpSession))
                    return false;

                if (dpSession.Step != AddWizardStep.Date)
                    return false;

                await HandleDatePickCallbackAsync(chatId, dpSession, update.CallbackQuery, dpData, ct);
                return true;
            }

            // ── Inline confirm/edit callbacks (add:*) ──
            if (update.CallbackQuery?.Data is { } data &&
                _store.TryGet(chatId, out var s1))
            {
                switch (data)
                {
                    case "add:cancel":
                        _store.Remove(chatId);
                        var cancelLang = await ResolveLanguageAsync(s1.UserId, ct);
                        await SafeEditAsync(update, chatId, _i18n.GetText(cancelLang, "wizard_cancelled"), ct);
                        await _bot.SendTextMessageAsync(chatId, _i18n.GetText(cancelLang, "wizard_next_action"),
                            replyMarkup: Keyboards.MainMenuKb(cancelLang),
                            cancellationToken: ct);
                        return true;

                    case "add:editname":
                        s1.Step = AddWizardStep.Name;
                        _store.Upsert(s1);
                        var editNameLang = await ResolveLanguageAsync(s1.UserId, ct);
                        await SafeEditAsync(update, chatId,
                            _i18n.GetText(editNameLang, "wizard_enter_name"),
                            ct, ParseMode.Html);
                        await _bot.SendTextMessageAsync(chatId, _i18n.GetText(editNameLang, "wizard_name_label"),
                            replyMarkup: NameKb(editNameLang), cancellationToken: ct);
                        return true;

                    case "add:editdate":
                        s1.Step = AddWizardStep.Date;
                        s1.CalendarMessageId = null;
                        s1.DatePickerDay = null;
                        s1.DatePickerMonth = null;
                        s1.DatePhase = DatePickerPhase.Day;
                        s1.YearPage = 0;
                        _store.Upsert(s1);
                        var editDateLang = await ResolveLanguageAsync(s1.UserId, ct);
                        await SafeEditAsync(update, chatId,
                            _i18n.GetText(editDateLang, "wizard_edit_date_prompt"),
                            ct, ParseMode.Html);
                        await SendDatePicker(chatId, s1, ct);
                        return true;

                    case "add:save" when s1.Name is not null && s1.Date is not null:
                        try
                        {
                            var user = await _users.GetByTelegramUserIdAsync(s1.UserId, ct);
                            if (user == null)
                            {
                                var fromUser = update.CallbackQuery?.From;
                                if (fromUser == null)
                                {
                                    var errorLang = await ResolveLanguageAsync(s1.UserId, ct);
                                    await SafeEditAsync(update, chatId, _i18n.GetText(errorLang, "error_try_again"), ct);
                                    return true;
                                }

                                user = new BirthdayBot.Domain.Entities.User
                                {
                                    TelegramUserId = fromUser.Id,
                                    Lang = BirthdayBot.Domain.Enums.Language.En,
                                    Tone = BirthdayBot.Domain.Enums.Tone.Friendly,
                                    Timezone = "Europe/Warsaw",
                                    NotifyAtLocalTime = "09:00",
                                    AutoGenerateGreetings = true
                                };
                                await _users.CreateAsync(user, ct);
                            }

                            var birthday = new Birthday
                            {
                                Name = s1.Name,
                                LastName = s1.LastName,
                                Date = s1.Date!.Value,
                                UserId = user.Id,
                                TimeZoneId = user.Timezone ?? "Europe/Warsaw",
                                Relation = s1.Relation,
                                Interests = s1.Interests,
                            };

                            await _birthdays.CreateAsync(birthday, ct);
                            _store.Remove(chatId);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Failed to save birthday for user {UserId}", s1.UserId);
                            var errorLang = await ResolveLanguageAsync(s1.UserId, ct);
                            await SafeEditAsync(update, chatId, _i18n.GetText(errorLang, "error_save_failed"), ct);
                            return true;
                        }

                        var fullName = string.IsNullOrWhiteSpace(s1.LastName)
                            ? Formatting.Html(s1.Name!)
                            : $"{Formatting.Html(s1.Name!)} {Formatting.Html(s1.LastName)}";

                        var savedLang = await ResolveLanguageAsync(s1.UserId, ct);
                        await SafeEditAsync(update, chatId,
                            string.Format(_i18n.GetText(savedLang, "wizard_saved_birthday"), fullName, $"{s1.Date:dd.MM.yyyy}"),
                            ct, ParseMode.Html);

                        await _bot.SendTextMessageAsync(chatId,
                            _i18n.GetText(savedLang, "wizard_next_action"),
                            replyMarkup: Keyboards.MainMenuKb(savedLang),
                            cancellationToken: ct);

                        return true;
                }
            }

            // ── Text-based wizard steps ──
            if (text is not null && _store.TryGet(chatId, out var s))
            {
                var lang = await ResolveLanguageAsync(s.UserId, ct);

                if (IsCancel(text, lang) || text.Equals("/cancel", StringComparison.OrdinalIgnoreCase))
                {
                    _store.Remove(chatId);
                    await _bot.SendTextMessageAsync(chatId, _i18n.GetText(lang, "wizard_cancelled"),
                        replyMarkup: new ReplyKeyboardRemove(), cancellationToken: ct);
                    await _bot.SendTextMessageAsync(chatId, _i18n.GetText(lang, "wizard_next_action"),
                        replyMarkup: Keyboards.MainMenuKb(lang), cancellationToken: ct);
                    return true;
                }

                switch (s.Step)
                {
                    // ① Name
                    case AddWizardStep.Name:
                        if (text.StartsWith('/')) return false;

                        var name = text.Trim();
                        if (name.Length is < 2 or > 64)
                        {
                            await _bot.SendTextMessageAsync(chatId,
                                _i18n.GetText(lang, "wizard_name_length_error"),
                                replyMarkup: NameKb(lang), cancellationToken: ct);
                            return true;
                        }

                        s.Name = name;
                        s.Step = AddWizardStep.LastName;
                        _store.Upsert(s);

                        await _bot.SendTextMessageAsync(chatId,
                            string.Format(_i18n.GetText(lang, "wizard_name_saved"),
                                Formatting.Html(name),
                                _i18n.GetText(lang, "ask_lastname")),
                            parseMode: ParseMode.Html,
                            replyMarkup: Keyboards.SkipCancelKb(lang),
                            cancellationToken: ct);

                        return true;

                    // ② Last Name (optional)
                    case AddWizardStep.LastName:
                        if (text.StartsWith('/')) return false;

                        if (IsSkip(text, lang))
                        {
                            s.LastName = null;
                        }
                        else
                        {
                            var ln = text.Trim();
                            if (ln.Length > 64)
                            {
                                await _bot.SendTextMessageAsync(chatId,
                                    _i18n.GetText(lang, "wizard_lastname_length_error"),
                                    replyMarkup: Keyboards.SkipCancelKb(lang),
                                    cancellationToken: ct);
                                return true;
                            }
                            s.LastName = ln;
                        }

                        s.Step = AddWizardStep.Date;
                        s.CalendarMessageId = null;
                        s.DatePickerDay = null;
                        s.DatePickerMonth = null;
                        s.DatePhase = DatePickerPhase.Day;
                        s.YearPage = 0;
                        _store.Upsert(s);

                        await _bot.SendTextMessageAsync(chatId,
                            _i18n.GetText(lang, "wizard_date_prompt"),
                            parseMode: ParseMode.Html,
                            replyMarkup: new ReplyKeyboardRemove(),
                            cancellationToken: ct);

                        await SendDatePicker(chatId, s, ct);
                        return true;

                    // ③ Date step — handled by dp:* callbacks above; text input falls through
                    case AddWizardStep.Date:
                        // Allow text fallback (e.g. "15.03.1990")
                        if (!TryParseDate(text, out var date))
                        {
                            await _bot.SendTextMessageAsync(chatId,
                                _i18n.GetText(lang, "wizard_date_parse_error"),
                                parseMode: ParseMode.Html,
                                cancellationToken: ct);
                            return true;
                        }

                        s.Date = date;
                        s.Step = AddWizardStep.Relation;
                        _store.Upsert(s);

                        await AskRelation(chatId, s.UserId, ct);
                        return true;

                    // ④ Relation (optional)
                    case AddWizardStep.Relation:
                        if (IsSkip(text, lang))
                            s.Relation = null;
                        else
                            s.Relation = text.Replace("👪 ", "").Replace("❤️ ", "")
                                             .Replace("🎓 ", "").Replace("💼 ", "").Trim();

                        s.Step = AddWizardStep.Interests;
                        _store.Upsert(s);

                        await _bot.SendTextMessageAsync(chatId,
                            _i18n.GetText(lang, "ask_interests"),
                            parseMode: ParseMode.Html,
                            replyMarkup: Keyboards.SkipCancelKb(lang),
                            cancellationToken: ct);
                        return true;

                    // ⑤ Interests (optional)
                    case AddWizardStep.Interests:
                        if (IsSkip(text, lang))
                            s.Interests = null;
                        else
                            s.Interests = text.Trim();

                        s.Step = AddWizardStep.Confirm;
                        _store.Upsert(s);

                        await SendConfirmation(chatId, s, ct);
                        return true;

                    // ⑥ Confirm — waiting for inline buttons only
                    case AddWizardStep.Confirm:
                        return true;
                }
            }

            return false;
        }
        catch (OperationCanceledException)
        {
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Wizard flow failed: {Message}", ex.Message);
            return false;
        }
    }

    // ════════════════════════════════════════════
    //  Date picker callback handler (dp:*)
    // ════════════════════════════════════════════

    private async Task HandleDatePickCallbackAsync(
        long chatId, AddBirthdayWizardSession s,
        CallbackQuery cq, string data, CancellationToken ct)
    {
        try
        {
            var lang = await ResolveLanguageAsync(s.UserId, ct);

            // ── Cancel ──────────────────────────────────────────────────────
            if (data == "dp:cancel")
            {
                _store.Remove(chatId);
                if (s.CalendarMessageId.HasValue)
                    await SafeEditPickerAsync(chatId, s.CalendarMessageId.Value,
                        _i18n.GetText(lang, "wizard_cancelled"), null, ct);
                await SafeAnswerCq(cq.Id, ct: ct);
                await _bot.SendTextMessageAsync(chatId, _i18n.GetText(lang, "wizard_next_action"),
                    replyMarkup: Keyboards.MainMenuKb(lang), cancellationToken: ct);
                return;
            }

            // ── Day selected ─────────────────────────────────────────────────
            if (data.StartsWith("dp:d:", StringComparison.Ordinal) &&
                int.TryParse(data["dp:d:".Length..], out var day) &&
                day is >= 1 and <= 31)
            {
                s.DatePickerDay = day;
                s.DatePhase = DatePickerPhase.Month;
                _store.Upsert(s);

                var headerText = DatePickerBuilder.FormHeader(lang, day, null, false)
                               + "\n\n" + _i18n.GetText(lang, "dp_pick_month");
                await SafeEditPickerAsync(chatId, s.CalendarMessageId,
                    headerText, DatePickerBuilder.MonthGrid(lang), ct);
                await SafeAnswerCq(cq.Id, ct: ct);
                return;
            }

            // ── Month selected ───────────────────────────────────────────────
            if (data.StartsWith("dp:m:", StringComparison.Ordinal) &&
                int.TryParse(data["dp:m:".Length..], out var month) &&
                month is >= 1 and <= 12)
            {
                // Validate day against selected month (use a non-leap year for check)
                var maxDay = MaxDayForMonth(month, null);
                if (s.DatePickerDay.HasValue && s.DatePickerDay.Value > maxDay)
                {
                    // Day is invalid for this month — bounce back to day picker
                    s.DatePickerDay = null;
                    s.DatePickerMonth = month;
                    s.DatePhase = DatePickerPhase.Day;
                    _store.Upsert(s);

                    var errText = _i18n.GetText(lang, "dp_day_invalid_for_month")
                                + "\n\n" + _i18n.GetText(lang, "dp_pick_day");
                    await SafeEditPickerAsync(chatId, s.CalendarMessageId,
                        errText, DatePickerBuilder.DayGrid(lang), ct);
                    await SafeAnswerCq(cq.Id, ct: ct);
                    return;
                }

                s.DatePickerMonth = month;
                s.DatePhase = DatePickerPhase.Year;
                s.YearPage = 0;
                _store.Upsert(s);

                var headerText = DatePickerBuilder.FormHeader(lang, s.DatePickerDay, month, true)
                               + "\n\n" + _i18n.GetText(lang, "dp_pick_year");
                await SafeEditPickerAsync(chatId, s.CalendarMessageId,
                    headerText, DatePickerBuilder.YearGrid(lang, 0), ct);
                await SafeAnswerCq(cq.Id, ct: ct);
                return;
            }

            // ── Year page navigation ─────────────────────────────────────────
            if (data.StartsWith("dp:yp:", StringComparison.Ordinal) &&
                int.TryParse(data["dp:yp:".Length..], out var page))
            {
                s.YearPage = page;
                _store.Upsert(s);

                var headerText = DatePickerBuilder.FormHeader(lang, s.DatePickerDay, s.DatePickerMonth, true)
                               + "\n\n" + _i18n.GetText(lang, "dp_pick_year");
                await SafeEditPickerAsync(chatId, s.CalendarMessageId,
                    headerText, DatePickerBuilder.YearGrid(lang, page), ct);
                await SafeAnswerCq(cq.Id, ct: ct);
                return;
            }

            // ── Year selected ────────────────────────────────────────────────
            if (data.StartsWith("dp:y:", StringComparison.Ordinal) &&
                int.TryParse(data["dp:y:".Length..], out var year))
            {
                await FinaliseDateAsync(chatId, s, year, lang, cq.Id, ct);
                return;
            }

            // ── No year ──────────────────────────────────────────────────────
            if (data == "dp:noyear")
            {
                await FinaliseDateAsync(chatId, s, year: null, lang, cq.Id, ct);
                return;
            }

            // ── Back to day picker ───────────────────────────────────────────
            if (data == "dp:back:d")
            {
                s.DatePickerDay = null;
                s.DatePhase = DatePickerPhase.Day;
                _store.Upsert(s);

                var headerText = DatePickerBuilder.FormHeader(lang, null, null, false)
                               + "\n\n" + _i18n.GetText(lang, "dp_pick_day");
                await SafeEditPickerAsync(chatId, s.CalendarMessageId,
                    headerText, DatePickerBuilder.DayGrid(lang), ct);
                await SafeAnswerCq(cq.Id, ct: ct);
                return;
            }

            // ── Back to month picker ─────────────────────────────────────────
            if (data == "dp:back:m")
            {
                s.DatePickerMonth = null;
                s.DatePhase = DatePickerPhase.Month;
                _store.Upsert(s);

                var headerText = DatePickerBuilder.FormHeader(lang, s.DatePickerDay, null, false)
                               + "\n\n" + _i18n.GetText(lang, "dp_pick_month");
                await SafeEditPickerAsync(chatId, s.CalendarMessageId,
                    headerText, DatePickerBuilder.MonthGrid(lang), ct);
                await SafeAnswerCq(cq.Id, ct: ct);
                return;
            }

            await SafeAnswerCq(cq.Id, ct: ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "DatePicker callback error: {Data}", data);
            await SafeAnswerCq(cq.Id,
                _i18n.GetText(await ResolveLanguageAsync(s.UserId, ct), "error_try_again"), ct);
        }
    }

    private async Task FinaliseDateAsync(
        long chatId, AddBirthdayWizardSession s,
        int? year, Language lang, string cqId, CancellationToken ct)
    {
        if (s.DatePickerDay is null || s.DatePickerMonth is null)
        {
            await SafeAnswerCq(cqId, ct: ct);
            return;
        }

        var resolvedYear = year ?? 1;
        DateOnly date;
        try
        {
            var maxDay = MaxDayForMonth(s.DatePickerMonth.Value, year);
            var clampedDay = Math.Min(s.DatePickerDay.Value, maxDay);
            date = new DateOnly(resolvedYear, s.DatePickerMonth.Value, clampedDay);
        }
        catch
        {
            await SafeAnswerCq(cqId, ct: ct);
            return;
        }

        s.Date = date;
        s.Step = AddWizardStep.Relation;
        _store.Upsert(s);

        var yearDisplay = year.HasValue ? year.Value.ToString() : "—";
        var monthName = DatePickerBuilder.MonthNames(lang)[s.DatePickerMonth.Value];
        var summary = lang switch
        {
            Language.Pl => $"📅 Wybrano: <b>{s.DatePickerDay} {monthName}" + (year.HasValue ? $" {year}" : "") + "</b>",
            Language.En => $"📅 Selected: <b>{s.DatePickerDay} {monthName}" + (year.HasValue ? $" {year}" : "") + "</b>",
            _            => $"📅 Выбрано: <b>{s.DatePickerDay} {monthName}" + (year.HasValue ? $" {year}" : "") + "</b>",
        };

        if (s.CalendarMessageId.HasValue)
            await SafeEditPickerAsync(chatId, s.CalendarMessageId.Value, summary, null, ct);

        await SafeAnswerCq(cqId, ct: ct);
        await AskRelation(chatId, s.UserId, ct);
    }

    // ── Shared prompts ────────────────────────────────────────────────────────

    private async Task AskRelation(long chatId, long userId, CancellationToken ct)
    {
        var lang = await ResolveLanguageAsync(userId, ct);
        await _bot.SendTextMessageAsync(chatId,
            _i18n.GetText(lang, "ask_relation"),
            parseMode: ParseMode.Html,
            replyMarkup: Keyboards.RelationKb(lang),
            cancellationToken: ct);
    }

    private async Task SendConfirmation(long chatId, AddBirthdayWizardSession s, CancellationToken ct)
    {
        var lang = await ResolveLanguageAsync(s.UserId, ct);
        var fullName = string.IsNullOrWhiteSpace(s.LastName)
            ? Formatting.Html(s.Name!)
            : $"{Formatting.Html(s.Name!)} {Formatting.Html(s.LastName)}";

        var relation = string.IsNullOrWhiteSpace(s.Relation)
            ? ""
            : $"👥 {Formatting.Html(s.Relation)}\n";
        var interests = string.IsNullOrWhiteSpace(s.Interests)
            ? ""
            : $"💡 {Formatting.Html(s.Interests)}\n";

        var dateDisplay = s.Date.HasValue
            ? (s.Date.Value.Year == 1
                ? $"{s.Date.Value:dd.MM}"
                : $"{s.Date.Value:dd.MM.yyyy}")
            : "—";

        var text = string.Format(
            _i18n.GetText(lang, "confirm_summary"),
            fullName,
            dateDisplay,
            relation,
            interests);

        await _bot.SendTextMessageAsync(chatId, text,
            parseMode: ParseMode.Html,
            replyMarkup: new ReplyKeyboardRemove(),
            cancellationToken: ct);

        await _bot.SendTextMessageAsync(chatId, _i18n.GetText(lang, "wizard_save_question"),
            replyMarkup: ConfirmKb(lang), cancellationToken: ct);
    }

    private async Task SendDatePicker(long chatId, AddBirthdayWizardSession s, CancellationToken ct)
    {
        var lang = await ResolveLanguageAsync(s.UserId, ct);
        var header = DatePickerBuilder.FormHeader(lang, null, null, false)
                   + "\n\n" + _i18n.GetText(lang, "dp_pick_day");

        var msg = await _bot.SendTextMessageAsync(chatId, header,
            parseMode: ParseMode.Html,
            replyMarkup: DatePickerBuilder.DayGrid(lang),
            cancellationToken: ct);

        s.CalendarMessageId = msg.MessageId;
        _store.Upsert(s);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static int MaxDayForMonth(int month, int? year)
    {
        // Use a known non-leap year when year is unknown, or the actual year
        var y = year ?? 2001;
        return DateTime.DaysInMonth(y, month);
    }

    private static bool TryParseDate(string input, out DateOnly date)
    {
        input = input.Trim();

        if (input.Equals("📅 Сегодня", StringComparison.OrdinalIgnoreCase) ||
            input.Equals("Сегодня", StringComparison.OrdinalIgnoreCase))
        { date = DateOnly.FromDateTime(DateTime.UtcNow); return true; }

        if (input.Equals("📅 Завтра", StringComparison.OrdinalIgnoreCase) ||
            input.Equals("Завтра", StringComparison.OrdinalIgnoreCase))
        { date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)); return true; }

        var m = DateRegex.Match(input);
        if (!m.Success) { date = default; return false; }

        var d  = int.Parse(m.Groups["d"].Value);
        var mm = int.Parse(m.Groups["m"].Value);
        var yr = m.Groups["y"].Success ? int.Parse(m.Groups["y"].Value) : DateTime.UtcNow.Year;

        return DateOnly.TryParse($"{yr:D4}-{mm:D2}-{d:D2}", out date);
    }

    // ── Safe Telegram API wrappers ────────────────────────────────────────────

    private async Task SafeEditAsync(Update update, long chatId, string text, CancellationToken ct, ParseMode? mode = null)
    {
        try
        {
            var msgId = update.CallbackQuery?.Message?.MessageId;
            if (msgId is not null)
            {
                await _bot.EditMessageTextAsync(chatId, msgId.Value, text, parseMode: mode, cancellationToken: ct);
                return;
            }

            await _bot.SendTextMessageAsync(chatId, text, parseMode: mode, cancellationToken: ct);
        }
        catch (ApiRequestException ex) when (
            ex.Message.Contains("message is not modified", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogDebug("EditMessageText skipped: message is not modified");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to edit message: {Message}", ex.Message);
            await _bot.SendTextMessageAsync(chatId, text, parseMode: mode, cancellationToken: ct);
        }
    }

    private async Task SafeEditPickerAsync(long chatId, int? msgId, string text, InlineKeyboardMarkup? kb, CancellationToken ct)
    {
        if (msgId is null) return;
        try
        {
            await _bot.EditMessageTextAsync(chatId, msgId.Value, text,
                parseMode: ParseMode.Html, replyMarkup: kb, cancellationToken: ct);
        }
        catch (ApiRequestException ex) when (
            ex.Message.Contains("message is not modified", StringComparison.OrdinalIgnoreCase))
        {
            // ignore
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to edit picker message");
        }
    }

    private async Task SafeAnswerCq(string id, string? text = null, CancellationToken ct = default)
    {
        try { await _bot.AnswerCallbackQueryAsync(id, text, cancellationToken: ct); }
        catch (Exception ex) { _logger.LogDebug(ex, "AnswerCallbackQuery failed"); }
    }

    private async Task<Language> ResolveLanguageAsync(long telegramUserId, CancellationToken ct)
    {
        try
        {
            var user = await _users.GetByTelegramUserIdAsync(telegramUserId, ct);
            return user?.Lang ?? Language.En;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to resolve language for user {UserId}", telegramUserId);
            return Language.En;
        }
    }

    private bool IsSkip(string text, Language lang) =>
        text.Equals(_i18n.GetText(lang, "skip"), StringComparison.OrdinalIgnoreCase) ||
        text.Equals(_i18n.GetText(Language.Ru, "skip"), StringComparison.OrdinalIgnoreCase);

    private bool IsCancel(string text, Language lang) =>
        text.Equals(_i18n.GetText(lang, "cancel"), StringComparison.OrdinalIgnoreCase) ||
        text.Equals(_i18n.GetText(Language.Ru, "cancel"), StringComparison.OrdinalIgnoreCase);
}

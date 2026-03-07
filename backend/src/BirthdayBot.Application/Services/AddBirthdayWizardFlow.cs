using System;
using System.Text.RegularExpressions;
using BirthdayBot.Application.Interfaces;
using BirthdayBot.Application.Models;
using BirthdayBot.Application.UI;
using BirthdayBot.Application.Utils;
using BirthdayBot.Domain.Entities;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace BirthdayBot.Application.Services;

/// <summary>
/// Multi-step wizard for adding a birthday:
/// Name → LastName → Date (calendar) → Relation → Interests → Confirm.
/// </summary>
public sealed class AddBirthdayWizardFlow : IWizardFlow
{
    private readonly ITelegramBotClient _bot;
    private readonly IConversationSessionStore _store;
    private readonly IBirthdayRepository _birthdays;
    private readonly IUserRepository _users;
    private readonly ILogger<AddBirthdayWizardFlow> _logger;

    private static readonly Regex DateRegex =
        new(@"^(?<d>\d{1,2})[.\-/](?<m>\d{1,2})(?:[.\-/](?<y>\d{4}))?$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

    // Reply keyboards
    private static readonly ReplyKeyboardMarkup NameKb = new(new[]
    {
        new KeyboardButton[] { "❌ Отмена" }
    })
    { ResizeKeyboard = true, OneTimeKeyboard = true };

    private static readonly ReplyKeyboardMarkup DateKb = new(new[]
    {
        new KeyboardButton[] { "📅 Сегодня", "📅 Завтра" },
        new KeyboardButton[] { "❌ Отмена" }
    })
    { ResizeKeyboard = true, OneTimeKeyboard = true };

    // Inline confirm keyboard
    private static readonly InlineKeyboardMarkup ConfirmKb = new(new[]
    {
        new [] { InlineKeyboardButton.WithCallbackData("✅ Сохранить", "add:save") },
        new [] { InlineKeyboardButton.WithCallbackData("✏️ Имя",      "add:editname"),
                 InlineKeyboardButton.WithCallbackData("📅 Дата",     "add:editdate") },
        new [] { InlineKeyboardButton.WithCallbackData("❌ Отмена",   "add:cancel") }
    });

    public AddBirthdayWizardFlow(
        ITelegramBotClient bot,
        IConversationSessionStore store,
        IBirthdayRepository birthdays,
        IUserRepository users,
        ILogger<AddBirthdayWizardFlow> logger)
    {
        _bot = bot;
        _store = store;
        _birthdays = birthdays;
        _users = users;
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
                var session = new AddBirthdayWizardSession(chatId, userId);
                _store.Upsert(session);

                await _bot.SendTextMessageAsync(
                    chatId,
                    "🎂 <b>Добавляем день рождения</b>\n\n" +
                    "① <b>Имя</b> → ② Фамилия → ③ Дата → ④ Кто это → ⑤ Интересы → ⑥ Подтверждение\n\n" +
                    "Введи <b>имя</b> именинника (например: <code>Маша</code>).",
                    parseMode: ParseMode.Html,
                    replyMarkup: NameKb,
                    cancellationToken: ct);

                return true;
            }

            // ── Calendar navigation callbacks (cal:*) ──
            if (update.CallbackQuery?.Data is { } cbData && cbData.StartsWith("cal:", StringComparison.Ordinal))
            {
                if (!_store.TryGet(chatId, out var calSession))
                    return false;

                if (calSession.Step != AddWizardStep.Date)
                    return false;

                await HandleCalendarCallbackAsync(chatId, calSession, update.CallbackQuery, cbData, ct);
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
                        await SafeEditAsync(update, chatId, "❌ Отменено", ct);
                        return true;

                    case "add:editname":
                        s1.Step = AddWizardStep.Name;
                        _store.Upsert(s1);
                        await SafeEditAsync(update, chatId,
                            "Введи <b>имя</b> (например: <code>Маша</code>).",
                            ct, ParseMode.Html);
                        await _bot.SendTextMessageAsync(chatId, "Имя:", replyMarkup: NameKb, cancellationToken: ct);
                        return true;

                    case "add:editdate":
                        s1.Step = AddWizardStep.Date;
                        s1.CalendarMessageId = null;
                        _store.Upsert(s1);
                        await SafeEditAsync(update, chatId,
                            "Введи <b>дату</b> или выбери в календаре.",
                            ct, ParseMode.Html);
                        await SendCalendar(chatId, s1, DateTime.UtcNow.Year, DateTime.UtcNow.Month, ct);
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
                                    await SafeEditAsync(update, chatId, "❌ Не удалось получить информацию о пользователе", ct);
                                    return true;
                                }

                                user = new BirthdayBot.Domain.Entities.User
                                {
                                    TelegramUserId = fromUser.Id,
                                    Lang = BirthdayBot.Domain.Enums.Language.Ru,
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
                            await SafeEditAsync(update, chatId, "❌ Ошибка сохранения", ct);
                            return true;
                        }

                        var fullName = string.IsNullOrWhiteSpace(s1.LastName)
                            ? Formatting.Html(s1.Name!)
                            : $"{Formatting.Html(s1.Name!)} {Formatting.Html(s1.LastName)}";

                        await SafeEditAsync(update, chatId,
                            $"✅ Сохранено!\n\n🎂 <b>{fullName}</b>, {s1.Date:dd.MM.yyyy}",
                            ct, ParseMode.Html);

                        // Show main menu after save
                        await _bot.SendTextMessageAsync(chatId,
                            "Что дальше?",
                            replyMarkup: Keyboards.MainMenuKb,
                            cancellationToken: ct);

                        return true;
                }
            }

            // ── Text-based wizard steps ──
            if (text is not null && _store.TryGet(chatId, out var s))
            {
                // Global cancel
                if (text.Equals("❌ Отмена", StringComparison.OrdinalIgnoreCase) || text.Equals("/cancel"))
                {
                    _store.Remove(chatId);
                    await _bot.SendTextMessageAsync(chatId, "❌ Отменено",
                        replyMarkup: new ReplyKeyboardRemove(), cancellationToken: ct);
                    await _bot.SendTextMessageAsync(chatId, "Что дальше?",
                        replyMarkup: Keyboards.MainMenuKb, cancellationToken: ct);
                    return true;
                }

                switch (s.Step)
                {
                    // ① Name
                    case AddWizardStep.Name:
                        if (text.StartsWith('/')) return false; // let other commands through

                        var name = text.Trim();
                        if (name.Length is < 2 or > 64)
                        {
                            await _bot.SendTextMessageAsync(chatId,
                                "Имя должно быть 2–64 символа. Попробуй ещё раз.",
                                replyMarkup: NameKb, cancellationToken: ct);
                            return true;
                        }

                        s.Name = name;
                        s.Step = AddWizardStep.LastName;
                        _store.Upsert(s);

                        await _bot.SendTextMessageAsync(chatId,
                            $"👤 Имя: <b>{Formatting.Html(name)}</b>\n\n" +
                            "Теперь введи <b>фамилию</b> (или нажми «Пропустить»).",
                            parseMode: ParseMode.Html,
                            replyMarkup: Keyboards.SkipCancelKb,
                            cancellationToken: ct);

                        return true;

                    // ② Last Name (optional)
                    case AddWizardStep.LastName:
                        if (text.StartsWith('/')) return false;

                        if (text == "➡️ Пропустить")
                        {
                            s.LastName = null;
                        }
                        else
                        {
                            var ln = text.Trim();
                            if (ln.Length > 64)
                            {
                                await _bot.SendTextMessageAsync(chatId,
                                    "Фамилия слишком длинная (макс. 64 символа).",
                                    replyMarkup: Keyboards.SkipCancelKb, cancellationToken: ct);
                                return true;
                            }
                            s.LastName = ln;
                        }

                        s.Step = AddWizardStep.Date;
                        s.CalendarMessageId = null;
                        _store.Upsert(s);

                        // Send calendar for date picking
                        var now = DateTime.UtcNow;
                        await _bot.SendTextMessageAsync(chatId,
                            "📅 Выбери <b>дату рождения</b> в календаре или введи вручную (<code>ДД.ММ.ГГГГ</code>).",
                            parseMode: ParseMode.Html,
                            replyMarkup: new ReplyKeyboardRemove(),
                            cancellationToken: ct);

                        await SendCalendar(chatId, s, now.Year, now.Month, ct);
                        return true;

                    // ③ Date (text fallback — calendar is primary)
                    case AddWizardStep.Date:
                        if (!TryParseDate(text, out var date))
                        {
                            await _bot.SendTextMessageAsync(chatId,
                                "Не понял дату. Введи <code>ДД.ММ</code> или <code>ДД.ММ.ГГГГ</code>, " +
                                "или выбери день в календаре выше.",
                                parseMode: ParseMode.Html,
                                cancellationToken: ct);
                            return true;
                        }

                        s.Date = date;
                        s.Step = AddWizardStep.Relation;
                        _store.Upsert(s);

                        await AskRelation(chatId, ct);
                        return true;

                    // ④ Relation (optional)
                    case AddWizardStep.Relation:
                        if (text == "➡️ Пропустить")
                            s.Relation = null;
                        else
                            s.Relation = text.Replace("👪 ", "").Replace("❤️ ", "")
                                             .Replace("🎓 ", "").Replace("💼 ", "").Trim();

                        s.Step = AddWizardStep.Interests;
                        _store.Upsert(s);

                        await _bot.SendTextMessageAsync(chatId,
                            "💡 Расскажи про <b>интересы/хобби</b> именинника " +
                            "(для персональных поздравлений).\n\n" +
                            "Например: <i>рыбалка, шахматы, кулинария</i>\n" +
                            "Или нажми «Пропустить».",
                            parseMode: ParseMode.Html,
                            replyMarkup: Keyboards.SkipCancelKb,
                            cancellationToken: ct);
                        return true;

                    // ⑤ Interests (optional)
                    case AddWizardStep.Interests:
                        if (text == "➡️ Пропустить")
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

    // ── Calendar callback handler ──

    private async Task HandleCalendarCallbackAsync(
        long chatId, AddBirthdayWizardSession s,
        CallbackQuery cq, string data, CancellationToken ct)
    {
        try
        {
            if (data == "cal:ignore")
            {
                await SafeAnswerCq(cq.Id, ct: ct);
                return;
            }

            if (data == "cal:cancel")
            {
                _store.Remove(chatId);
                if (s.CalendarMessageId.HasValue)
                {
                    await SafeEditCalendarAsync(chatId, s.CalendarMessageId.Value,
                        "❌ Отменено", null, ct);
                }
                await SafeAnswerCq(cq.Id, ct: ct);
                await _bot.SendTextMessageAsync(chatId, "Что дальше?",
                    replyMarkup: Keyboards.MainMenuKb, cancellationToken: ct);
                return;
            }

            if (data == "cal:manual")
            {
                // Switch to manual text entry mode
                if (s.CalendarMessageId.HasValue)
                {
                    await SafeEditCalendarAsync(chatId, s.CalendarMessageId.Value,
                        "⌨️ Введи дату вручную: <code>ДД.ММ.ГГГГ</code>", null, ct);
                }
                await SafeAnswerCq(cq.Id, ct: ct);
                return;
            }

            // cal:prev:YYYY-MM or cal:next:YYYY-MM — navigate calendar
            if (data.StartsWith("cal:prev:", StringComparison.Ordinal) ||
                data.StartsWith("cal:next:", StringComparison.Ordinal))
            {
                var parts = data[(data.IndexOf(':', 4) + 1)..].Split('-');
                if (parts.Length == 2 &&
                    int.TryParse(parts[0], out var year) &&
                    int.TryParse(parts[1], out var month) &&
                    month is >= 1 and <= 12)
                {
                    var calendar = InlineCalendarBuilder.BuildMonthGrid(year, month);
                    if (s.CalendarMessageId.HasValue)
                    {
                        try
                        {
                            await _bot.EditMessageReplyMarkupAsync(chatId, s.CalendarMessageId.Value,
                                replyMarkup: calendar, cancellationToken: ct);
                        }
                        catch (ApiRequestException ex) when (
                            ex.Message.Contains("message is not modified", StringComparison.OrdinalIgnoreCase))
                        {
                            // ignore
                        }
                    }
                }

                await SafeAnswerCq(cq.Id, ct: ct);
                return;
            }

            // cal:day:YYYY-MM-DD — day selected
            if (data.StartsWith("cal:day:", StringComparison.Ordinal))
            {
                var dateStr = data["cal:day:".Length..];
                if (DateOnly.TryParse(dateStr, out var date))
                {
                    s.Date = date;
                    s.Step = AddWizardStep.Relation;
                    _store.Upsert(s);

                    // Update calendar message to show selected date
                    if (s.CalendarMessageId.HasValue)
                    {
                        await SafeEditCalendarAsync(chatId, s.CalendarMessageId.Value,
                            $"📅 Выбрана дата: <b>{date:dd.MM.yyyy}</b>", null, ct);
                    }

                    await SafeAnswerCq(cq.Id, ct: ct);
                    await AskRelation(chatId, ct);
                    return;
                }
            }

            await SafeAnswerCq(cq.Id, ct: ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Calendar callback error: {Data}", data);
            await SafeAnswerCq(cq.Id, "Ошибка", ct);
        }
    }

    // ── Shared prompts ──

    private async Task AskRelation(long chatId, CancellationToken ct)
    {
        await _bot.SendTextMessageAsync(chatId,
            "👥 <b>Кто этот человек для тебя?</b>\n" +
            "Выбери кнопку или введи свой вариант.",
            parseMode: ParseMode.Html,
            replyMarkup: Keyboards.RelationKb,
            cancellationToken: ct);
    }

    private async Task SendConfirmation(long chatId, AddBirthdayWizardSession s, CancellationToken ct)
    {
        var fullName = string.IsNullOrWhiteSpace(s.LastName)
            ? Formatting.Html(s.Name!)
            : $"{Formatting.Html(s.Name!)} {Formatting.Html(s.LastName)}";

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("📋 <b>Проверим данные:</b>\n");
        sb.AppendLine($"👤 <b>{fullName}</b>");
        sb.AppendLine($"📅 {s.Date:dd.MM.yyyy}");

        if (!string.IsNullOrWhiteSpace(s.Relation))
            sb.AppendLine($"👥 {Formatting.Html(s.Relation)}");

        if (!string.IsNullOrWhiteSpace(s.Interests))
            sb.AppendLine($"💡 {Formatting.Html(s.Interests)}");

        sb.AppendLine("\nВсё верно?");

        await _bot.SendTextMessageAsync(chatId, sb.ToString(),
            parseMode: ParseMode.Html,
            replyMarkup: new ReplyKeyboardRemove(),
            cancellationToken: ct);

        await _bot.SendTextMessageAsync(chatId, "Сохранить?",
            replyMarkup: ConfirmKb, cancellationToken: ct);
    }

    private async Task SendCalendar(long chatId, AddBirthdayWizardSession s, int year, int month, CancellationToken ct)
    {
        var calendar = InlineCalendarBuilder.BuildMonthGrid(year, month);
        var msg = await _bot.SendTextMessageAsync(chatId,
            "📅 Выбери день:",
            replyMarkup: calendar,
            cancellationToken: ct);

        s.CalendarMessageId = msg.MessageId;
        _store.Upsert(s);
    }

    // ── Date parsing ──

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

        var d = int.Parse(m.Groups["d"].Value);
        var mm = int.Parse(m.Groups["m"].Value);
        var year = m.Groups["y"].Success ? int.Parse(m.Groups["y"].Value) : DateTime.UtcNow.Year;

        return DateOnly.TryParse($"{year:D4}-{mm:D2}-{d:D2}", out date);
    }

    // ── Safe Telegram API wrappers ──

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

    private async Task SafeEditCalendarAsync(long chatId, int msgId, string text, InlineKeyboardMarkup? kb, CancellationToken ct)
    {
        try
        {
            await _bot.EditMessageTextAsync(chatId, msgId, text,
                parseMode: ParseMode.Html, replyMarkup: kb, cancellationToken: ct);
        }
        catch (ApiRequestException ex) when (
            ex.Message.Contains("message is not modified", StringComparison.OrdinalIgnoreCase))
        {
            // ignore
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to edit calendar message");
        }
    }

    private async Task SafeAnswerCq(string id, string? text = null, CancellationToken ct = default)
    {
        try { await _bot.AnswerCallbackQueryAsync(id, text, cancellationToken: ct); }
        catch (Exception ex) { _logger.LogDebug(ex, "AnswerCallbackQuery failed"); }
    }
}

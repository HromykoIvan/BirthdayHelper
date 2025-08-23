using System;
using System.Text.RegularExpressions;
using BirthdayBot.Application.Interfaces;
using BirthdayBot.Application.Models;
using BirthdayBot.Domain.Entities;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace BirthdayBot.Application.Services;

/// <summary>
/// Пошаговый мастер добавления дня рождения: Имя → Дата → Подтверждение.
/// Внутренне, для сохранения, делегирует в существующий IUpdateHandler
/// (проксируя команду /add_birthday), поэтому не ломает текущую бизнес-логику.
/// </summary>
public sealed class AddBirthdayWizardFlow : IWizardFlow
{
    private readonly ITelegramBotClient _bot;
    private readonly IConversationSessionStore _store;
    private readonly IBirthdayRepository _birthdays;
    private readonly IUserRepository _users;
    private readonly ILogger<AddBirthdayWizardFlow> _logger;

    // Компилированный регекс — меньше аллокаций и быстрее парсинг дат
    private static readonly Regex DateRegex =
        new(@"^(?<d>\d{1,2})[.\-/](?<m>\d{1,2})(?:[.\-/](?<y>\d{4}))?$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

    // Клавиатуры создаём один раз — это дешёвый кеш
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
            // Извлекаем chatId/userId из Message/CallbackQuery
            var chatId = update.Message?.Chat.Id
                         ?? update.CallbackQuery?.Message?.Chat.Id
                         ?? 0;
            var userId = update.Message?.From?.Id
                         ?? update.CallbackQuery?.From.Id
                         ?? 0;

            if (chatId == 0 || userId == 0)
                return false; // игнорируем каналы/системные апдейты — отдаём дальше по пайплайну

            var text = update.Message?.Text;

            // 1) старт мастера строго по /add_birthday
            if (text is "/add_birthday")
            {
                var session = new AddBirthdayWizardSession(chatId, userId);
                _store.Upsert(session);

                await _bot.SendTextMessageAsync(
                    chatId,
                    "Давай добавим день рождения.\n" +
                    "Укажи *имя* (например: `Маша`).",
                    parseMode: ParseMode.Markdown,
                    replyMarkup: NameKb,
                    cancellationToken: ct);

                return true;
            }

            // 2) обработка callback-кнопок подтверждения
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
                            "Укажи *имя* (например: `Маша`).",
                            ct, ParseMode.Markdown);
                        await _bot.SendTextMessageAsync(chatId, "Имя:", replyMarkup: NameKb, cancellationToken: ct);
                        return true;

                    case "add:editdate":
                        s1.Step = AddWizardStep.Date;
                        _store.Upsert(s1);
                        await SafeEditAsync(update, chatId,
                            "Укажи *дату* в формате `ДД.ММ` или `ДД.ММ.ГГГГ`.",
                            ct, ParseMode.Markdown);
                        await _bot.SendTextMessageAsync(chatId, "Дата:", replyMarkup: DateKb, cancellationToken: ct);
                        return true;

                    case "add:save" when s1.Name is not null && s1.Date is not null:
                        // Прямо создаём день рождения
                        try
                        {
                            var user = await _users.GetByTelegramUserIdAsync(s1.UserId, ct);
                            if (user == null)
                            {
                                await SafeEditAsync(update, chatId, "❌ Пользователь не найден", ct);
                                return true;
                            }

                            var birthday = new Birthday
                            {
                                Name = s1.Name,
                                Date = s1.Date!.Value,
                                UserId = s1.UserId,
                                TimeZoneId = user.Timezone ?? "Europe/Warsaw"
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

                        await SafeEditAsync(update, chatId,
                            $"✅ Сохранено: *{s1.Name}*, {s1.Date:dd.MM.yyyy}",
                            ct, ParseMode.Markdown);

                        return true;
                }
            }

            // 3) если идёт активная сессия — обрабатываем шаги по тексту
            if (text is not null && _store.TryGet(chatId, out var s))
            {
                // глобальная отмена
                if (text.Equals("❌ Отмена", StringComparison.OrdinalIgnoreCase) || text.Equals("/cancel"))
                {
                    _store.Remove(chatId);
                    await _bot.SendTextMessageAsync(chatId, "❌ Отменено",
                        replyMarkup: new ReplyKeyboardRemove(), cancellationToken: ct);
                    return true;
                }

                switch (s.Step)
                {
                    case AddWizardStep.Name:
                        if (text.StartsWith('/')) // пользователь решил ввести команду — отдадим дальше
                            return false;

                        var name = text.Trim();
                        if (name.Length is < 2 or > 64)
                        {
                            await _bot.SendTextMessageAsync(chatId,
                                "Имя должно быть 2–64 символа. Попробуй ещё раз 🙂",
                                replyMarkup: NameKb, cancellationToken: ct);
                            return true;
                        }

                        s.Name = name;
                        s.Step = AddWizardStep.Date;
                        _store.Upsert(s);

                        await _bot.SendTextMessageAsync(chatId,
                            "Отлично! Теперь укажи *дату* в формате `ДД.ММ` или `ДД.ММ.ГГГГ`.\n" +
                            "Можно нажать «Сегодня/Завтра».",
                            parseMode: ParseMode.Markdown,
                            replyMarkup: DateKb,
                            cancellationToken: ct);

                        return true;

                    case AddWizardStep.Date:
                        if (!TryParseDate(text, out var date))
                        {
                            await _bot.SendTextMessageAsync(chatId,
                                "Не понял дату 🤔 Введи `ДД.ММ` или `ДД.ММ.ГГГГ` (например, `05.11.1990`).",
                                parseMode: ParseMode.Markdown,
                                replyMarkup: DateKb, cancellationToken: ct);
                            return true;
                        }

                        s.Date = date;
                        s.Step = AddWizardStep.Confirm;
                        _store.Upsert(s);

                        await _bot.SendTextMessageAsync(chatId,
                            $"Проверим:\n*{s.Name}* — {s.Date:dd.MM.yyyy}",
                            parseMode: ParseMode.Markdown,
                            replyMarkup: new ReplyKeyboardRemove(),
                            cancellationToken: ct);

                        await _bot.SendTextMessageAsync(chatId, "Сохранить?",
                            replyMarkup: ConfirmKb, cancellationToken: ct);

                        return true;

                    case AddWizardStep.Confirm:
                        // На этом шаге ждём только нажатия инлайн-кнопок; текст не обрабатываем
                        return true;
                }
            }

            // 4) Ничего не наше — отдаём дальше (в общий обработчик команд)
            return false;
        }
        catch (OperationCanceledException)
        {
            // штатная отмена — не логируем как ошибку
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Wizard flow failed: {Message}", ex.Message);
            // Не роняем пайплайн — пусть общий обработчик попробует сам
            return false;
        }
    }

    private static bool TryParseDate(string input, out DateOnly date)
    {
        input = input.Trim();

        if (input.Equals("📅 Сегодня", StringComparison.OrdinalIgnoreCase))
        { date = DateOnly.FromDateTime(DateTime.UtcNow); return true; }

        if (input.Equals("📅 Завтра", StringComparison.OrdinalIgnoreCase))
        { date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)); return true; }

        var m = DateRegex.Match(input);
        if (!m.Success) { date = default; return false; }

        var d = int.Parse(m.Groups["d"].Value);
        var mm = int.Parse(m.Groups["m"].Value);
        var year = m.Groups["y"].Success ? int.Parse(m.Groups["y"].Value) : DateTime.UtcNow.Year;

        return DateOnly.TryParse($"{year:D4}-{mm:D2}-{d:D2}", out date);
    }

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

            // если редактировать нечего — отправим новое
            await _bot.SendTextMessageAsync(chatId, text, parseMode: mode, cancellationToken: ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to edit message: {Message}", ex.Message);
            // молча отправим новое
            await _bot.SendTextMessageAsync(chatId, text, parseMode: mode, cancellationToken: ct);
        }
    }
}
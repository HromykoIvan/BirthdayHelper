using System.Globalization;
using System.Text;
using BirthdayBot.Application.Interfaces;
using BirthdayBot.Application.UI;
using BirthdayBot.Application.Services;
using static BirthdayBot.Application.UI.Keyboards;
using BirthdayBot.Domain.Entities;
using BirthdayBot.Domain.Enums;
using BirthdayBot.Domain.Utils;
using MongoDB.Bson;
using NodaTime;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using Microsoft.Extensions.Logging;

namespace BirthdayBot.Infrastructure.Services;

/// <summary>
/// Главный обработчик апдейтов Telegram. 
/// Приоритеты:
/// 1) Мастер добавления дня рождения (если активен) — "съедает" апдейт.
/// 2) Inline-фильтры "up:*" для выборок.
/// 3) Команды (/start, /help, /add_birthday, /list, /remove, /settings) и прочее.
/// </summary>
public sealed class UpdateHandler : IUpdateHandler
{
    private readonly ILogger<UpdateHandler> _logger;
    private readonly ITelegramBotClient _bot;
    private readonly IUserRepository _users;
    private readonly IBirthdayRepository _birthdays;
    private readonly ILocalizationService _i18n;
    private readonly IUpcomingService _upcoming;
    private readonly AddBirthdayWizardFlow _wizard;
    private readonly IDateTimeZoneProvider _tzdb;

    public UpdateHandler(
        ILogger<UpdateHandler> logger,
        ITelegramBotClient bot,
        IUserRepository users,
        IBirthdayRepository birthdays,
        ILocalizationService i18n,
        IUpcomingService upcoming,
        AddBirthdayWizardFlow wizard,
        IDateTimeZoneProvider? tzdb = null)
    {
        _logger = logger;
        _bot = bot;
        _users = users;
        _birthdays = birthdays;
        _i18n = i18n;
        _upcoming = upcoming;
        _wizard = wizard;
        _tzdb = tzdb ?? DateTimeZoneProviders.Tzdb;
    }

    public async Task HandleUpdateAsync(Update update, CancellationToken ct)
    {
        try
        {
            // 0) Всегда пытаемся отдать апдейт мастеру — он сам решит, активна ли сессия.
            if (await _wizard.TryHandleAsync(update, ct))
                return;

            // 1) Inline-фильтры по ближайшим ДР ("up:today|tomorrow|7|this|next")
            var data = update.CallbackQuery?.Data;
            if (!string.IsNullOrEmpty(data) && data.StartsWith("up:", StringComparison.Ordinal))
            {
                await HandleUpcomingCallbackAsync(update, data, ct);
                return;
            }

            // 2) Команды / сообщения
            switch (update.Type)
            {
                case UpdateType.Message when update.Message!.Text is not null:
                    await HandleTextMessageAsync(update.Message!, ct);
                    break;

                case UpdateType.CallbackQuery:
                    await HandleCallbackQueryAsync(update.CallbackQuery!, ct);
                    break;

                default:
                    // игнорируем прочие типы
                    break;
            }
        }
        catch (OperationCanceledException)
        {
            // ожидаемая отмена — не логируем как ошибку
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled error while processing update {UpdateId}", update.Id);

            // В случае callback'а — корректно отвечаем, чтобы Телеграм не висел
            if (update.CallbackQuery?.Id is { } cqid)
            {
                await SafeAnswerCallbackQuery(cqid, "Произошла ошибка. Попробуйте ещё раз.", ct);
            }
        }
    }

    // ---------- Text messages ----------

    private async Task HandleTextMessageAsync(Message msg, CancellationToken ct)
    {
        var chatId = msg.Chat.Id;
        var text = msg.Text!.Trim();
        var user = await EnsureUser(msg.From!, ct);

        if (text.StartsWith("/start", StringComparison.OrdinalIgnoreCase) ||
            text.StartsWith("/help", StringComparison.OrdinalIgnoreCase))
        {
            await _bot.SendTextMessageAsync(chatId, _i18n.GetText(user.Lang, "start"), cancellationToken: ct);
            return;
        }

        if (text.StartsWith("/add_birthday", StringComparison.OrdinalIgnoreCase))
        {
            // Запуск мастер-диалога добавления дня рождения.
            // Подбери нужную тебе сигнатуру, если у твоего класса она другая:
            await _wizard.TryHandleAsync(new Update { Message = new Message { Chat = new Chat { Id = chatId }, From = new Telegram.Bot.Types.User { Id = user.TelegramUserId } } }, ct);
            return;
        }

        if (text.StartsWith("/list", StringComparison.OrdinalIgnoreCase))
        {
            // Покажем фильтры + текущий список (с кнопками удаления)
            await _bot.SendTextMessageAsync(chatId, "Выберите период:", replyMarkup: BirthdayBot.Application.UI.Keyboards.UpcomingKb, cancellationToken: ct);
            await SendFullListWithDeleteButtons(user, chatId, ct);
            return;
        }

        if (text.StartsWith("/remove", StringComparison.OrdinalIgnoreCase))
        {
            var name = text.Replace("/remove", "", StringComparison.OrdinalIgnoreCase).Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                await _bot.SendTextMessageAsync(chatId, "Usage: /remove <name>", cancellationToken: ct);
                return;
            }

            var b = await _birthdays.FindByNameAsync(user.Id, name, ct);
            if (b == null)
            {
                await _bot.SendTextMessageAsync(chatId, "Not found.", cancellationToken: ct);
                return;
            }

            await _birthdays.DeleteAsync(b.Id, user.Id, ct);
            await _bot.SendTextMessageAsync(chatId, _i18n.GetText(user.Lang, "removed"), cancellationToken: ct);
            return;
        }

        if (text.StartsWith("/settings", StringComparison.OrdinalIgnoreCase))
        {
            await _bot.SendTextMessageAsync(chatId, _i18n.GetText(user.Lang, "settings_prompt"), cancellationToken: ct);
            return;
        }

        // Простые "смарт-настройки" по свободному тексту (как было у тебя)
        // Важно: оставляем, но не мешаем мастеру (он уже отработал выше).
        if (await TryApplyLooseSettingsAsync(user, chatId, text, ct))
            return;

        // Фоллбэк
        await _bot.SendTextMessageAsync(chatId, _i18n.GetText(user.Lang, "start"), cancellationToken: ct);
    }

    private async Task<bool> TryApplyLooseSettingsAsync(BirthdayBot.Domain.Entities.User user, long chatId, string text, CancellationToken ct)
    {
        var updated = false;

        if (DateHelpers.TryParseTimeHHmm(text, out var h, out var m))
        {
            user.NotifyAtLocalTime = $"{h:00}:{m:00}";
            updated = true;
        }

        if (text.Contains("ru", StringComparison.OrdinalIgnoreCase)) { user.Lang = Language.Ru; updated = true; }
        if (text.Contains("pl", StringComparison.OrdinalIgnoreCase)) { user.Lang = Language.Pl; updated = true; }
        if (text.Contains("en", StringComparison.OrdinalIgnoreCase)) { user.Lang = Language.En; updated = true; }

        if (text.Contains("formal", StringComparison.OrdinalIgnoreCase)) { user.Tone = Tone.Formal; updated = true; }
        if (text.Contains("friendly", StringComparison.OrdinalIgnoreCase)) { user.Tone = Tone.Friendly; updated = true; }

        if (text.Contains("auto on", StringComparison.OrdinalIgnoreCase)) { user.AutoGenerateGreetings = true; updated = true; }
        if (text.Contains("auto off", StringComparison.OrdinalIgnoreCase)) { user.AutoGenerateGreetings = false; updated = true; }

        // IANA tz
        if (_tzdb.Ids.Contains(text))
        {
            user.Timezone = text;
            updated = true;
        }

        if (!updated) return false;

        await _users.UpdateAsync(user, ct);
        await _bot.SendTextMessageAsync(chatId, _i18n.GetText(user.Lang, "saved"), cancellationToken: ct);
        return true;
    }

    // ---------- CallbackQuery ----------

    private async Task HandleCallbackQueryAsync(CallbackQuery cq, CancellationToken ct)
    {
        var user = await EnsureUser(cq.From, ct);

        try
        {
            if (cq.Data is { } data && data.StartsWith("delete:", StringComparison.Ordinal))
            {
                var idStr = data["delete:".Length..];
                if (ObjectId.TryParse(idStr, out var bid))
                {
                    await _birthdays.DeleteAsync(bid, user.Id, ct);

                    // Можем отредактировать исходное сообщение, чтобы не дублировать
                    if (cq.Message is not null)
                    {
                        await _bot.EditMessageTextAsync(
                            user.TelegramUserId,
                            cq.Message.MessageId,
                            _i18n.GetText(user.Lang, "removed"),
                            cancellationToken: ct);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to handle callback '{Data}'", cq.Data);
            await SafeAnswerCallbackQuery(cq.Id, "Ошибка. Попробуйте ещё раз.", ct);
            return;
        }

        await SafeAnswerCallbackQuery(cq.Id);
    }

    // ---------- Upcoming filters (up:*) ----------

    private async Task HandleUpcomingCallbackAsync(Update update, string data, CancellationToken ct)
    {
        try
        {
            var cq = update.CallbackQuery!;
            var chatId = cq.Message!.Chat.Id;
            var user = await EnsureUser(cq.From, ct);

            var kind = data[3..]; // после "up:"
            var zone = _tzdb[user.Timezone];
            var today = SystemClock.Instance.GetCurrentInstant().InZone(zone).Date;

            var (from, to) = kind switch
            {
                "today"    => (today, today),
                "tomorrow" => (today.PlusDays(1), today.PlusDays(1)),
                "7"        => (today, today.PlusDays(7)),
                "this"     => (new LocalDate(today.Year, today.Month, 1),
                               new LocalDate(today.Year, today.Month, DateTime.DaysInMonth(today.Year, today.Month))),
                "next"     => (FirstDayOfNextMonth(today), LastDayOfNextMonth(today)),
                _          => (today, today.PlusDays(7))
            };

            var list = await _birthdays.ListByUserAsync(user.Id, ct);

            // Выбираем те записи, у которых "следующий ДР от today" попадает в диапазон [from..to]
            var items = list
                .Select(b =>
                {
                    var (next, age) = DateHelpers.NextBirthday(today, b.Date);
                    return new UpcomingRow(b.Name, next, age);
                })
                .Where(x => x.Date >= from && x.Date <= to)
                .OrderBy(x => x.Date)
                .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var text = items.Length == 0
                ? "🎉 В ближайший период дней рождения нет."
                : BuildUpcomingList(items);

            await _bot.EditMessageTextAsync(
                chatId,
                cq.Message!.MessageId,
                text,
                parseMode: ParseMode.Markdown,
                replyMarkup: BirthdayBot.Application.UI.Keyboards.UpcomingKb,
                cancellationToken: ct);

            await SafeAnswerCallbackQuery(cq.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to handle upcoming filter callback: {Data}", data);
            if (update.CallbackQuery is { Id: not null } cq)
                await SafeAnswerCallbackQuery(cq.Id, "Произошла ошибка. Попробуйте ещё раз.", ct);
        }

        static LocalDate FirstDayOfNextMonth(LocalDate d)
        {
            var (y, m) = d.Month == 12 ? (d.Year + 1, 1) : (d.Year, d.Month + 1);
            return new LocalDate(y, m, 1);
        }

        static LocalDate LastDayOfNextMonth(LocalDate d)
        {
            var first = FirstDayOfNextMonth(d);
            var (y, m) = first.Month == 12 ? (first.Year + 1, 1) : (first.Year, first.Month + 1);
            var lastDay = new LocalDate(y, m, 1).PlusDays(-1); // последний день next-месяца
            return lastDay;
        }
    }

    private static string BuildUpcomingList(IEnumerable<UpcomingRow> items)
    {
        var sb = new StringBuilder();
        foreach (var i in items)
        {
            sb.AppendLine($"• *{EscapeMd(i.Name)}* — `{i.Date:yyyy-MM-dd}`, turns *{i.Age}*");
        }
        return sb.ToString();
    }

    private record struct UpcomingRow(string Name, LocalDate Date, int Age);

    // ---------- Helpers ----------

    private async Task<BirthdayBot.Domain.Entities.User> EnsureUser(Telegram.Bot.Types.User tgUser, CancellationToken ct)
    {
        var existing = await _users.GetByTelegramUserIdAsync(tgUser.Id, ct);
        if (existing is not null) return existing;

        var created = new BirthdayBot.Domain.Entities.User
        {
            TelegramUserId = tgUser.Id,
            Timezone = "Europe/Warsaw",
            NotifyAtLocalTime = "09:00",
            Lang = Language.Ru,
            AutoGenerateGreetings = true,
            Tone = Tone.Friendly,
            CreatedAt = DateTime.UtcNow
        };

        await _users.CreateAsync(created, ct);
        return created;
    }

    /// <summary>Отправляет полный список с инлайн-кнопками удаления.</summary>
    private async Task SendFullListWithDeleteButtons(BirthdayBot.Domain.Entities.User user, long chatId, CancellationToken ct)
    {
        var list = await _birthdays.ListByUserAsync(user.Id, ct);
        if (list.Count == 0)
        {
            await _bot.SendTextMessageAsync(chatId, _i18n.GetText(user.Lang, "list_empty"), cancellationToken: ct);
            return;
        }

        var zone = _tzdb[user.Timezone];
        var today = SystemClock.Instance.GetCurrentInstant().InZone(zone).Date;

        // Формируем компактные строки + клавиатуру "Delete"
        var lines = new List<string>(list.Count);
        var rows = new List<InlineKeyboardButton[]>(list.Count);

        foreach (var b in list.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
        {
            var (next, age) = DateHelpers.NextBirthday(today, b.Date);
            lines.Add($"{EscapeMd(b.Name)}: {b.Date:yyyy-MM-dd} → next {next:yyyy-MM-dd}, turns {age}");

            rows.Add(new[]
            {
                InlineKeyboardButton.WithCallbackData($"Delete {b.Name}", $"delete:{b.Id}")
            });
        }

        var text = string.Join('\n', lines);
        await _bot.SendTextMessageAsync(chatId, text, replyMarkup: new InlineKeyboardMarkup(rows), cancellationToken: ct);
    }

    private static string EscapeMd(string s)
        => s.Replace("_", "\\_").Replace("*", "\\*").Replace("[", "\\["); // безопасный markdown-lite

    private async Task SafeAnswerCallbackQuery(string id, string? text = null, CancellationToken ct = default)
    {
        try { await _bot.AnswerCallbackQueryAsync(id, text, cancellationToken: ct); }
        catch (Exception ex) { _logger.LogDebug(ex, "AnswerCallbackQuery failed"); }
    }
}
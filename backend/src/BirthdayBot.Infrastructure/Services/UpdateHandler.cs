using System.Globalization;
using System.Text;
using BirthdayBot.Application.Interfaces;
using BirthdayBot.Application.UI;
using BirthdayBot.Application.Services;
using BirthdayBot.Application.Utils;
using BirthdayBot.Domain.Entities;
using BirthdayBot.Domain.Enums;
using BirthdayBot.Domain.Utils;
using MongoDB.Bson;
using NodaTime;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using Microsoft.Extensions.Logging;

namespace BirthdayBot.Infrastructure.Services;

/// <summary>
/// Main Telegram update handler.
/// Priority order:
/// 1) Add-birthday wizard (if session active) — consumes the update.
/// 2) Inline callbacks (menu:*, up:*, list:*, cal:*, delete:*).
/// 3) Text commands (/start, /help, /add_birthday, /list, /remove, /settings).
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
            // 0) Always try the wizard first — it decides whether a session is active.
            if (await _wizard.TryHandleAsync(update, ct))
                return;

            // 1) Inline callbacks
            var data = update.CallbackQuery?.Data;
            if (!string.IsNullOrEmpty(data))
            {
                if (data.StartsWith("menu:", StringComparison.Ordinal))
                {
                    await HandleMenuCallbackAsync(update, data, ct);
                    return;
                }

                if (data.StartsWith("up:", StringComparison.Ordinal))
                {
                    await HandleUpcomingCallbackAsync(update, data, ct);
                    return;
                }

                if (data.StartsWith("list:", StringComparison.Ordinal))
                {
                    await HandleListCallbackAsync(update, data, ct);
                    return;
                }

                if (data == "cal:ignore")
                {
                    // Non-clickable calendar header cells — just acknowledge
                    await SafeAnswerCallbackQuery(update.CallbackQuery!.Id, ct: ct);
                    return;
                }
            }

            // 2) Text commands / messages
            switch (update.Type)
            {
                case UpdateType.Message when update.Message!.Text is not null:
                    await HandleTextMessageAsync(update.Message!, ct);
                    break;

                case UpdateType.CallbackQuery:
                    await HandleCallbackQueryAsync(update.CallbackQuery!, ct);
                    break;
            }
        }
        catch (OperationCanceledException)
        {
            // Expected cancellation — don't log as error
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled error while processing update {UpdateId}", update.Id);

            if (update.CallbackQuery?.Id is { } cqid)
            {
                await SafeAnswerCallbackQuery(cqid, "Произошла ошибка. Попробуйте ещё раз.", ct);
            }
        }
    }

    // ════════════════════════════════════════════
    //  Text messages
    // ════════════════════════════════════════════

    private async Task HandleTextMessageAsync(Message msg, CancellationToken ct)
    {
        if (msg is null) return;

        var chatId = msg.Chat?.Id ?? msg.From?.Id ?? 0;
        if (chatId == 0) return;

        var text = msg.Text!.Trim();
        var user = await EnsureUser(msg.From!, ct);

        if (text.StartsWith("/start", StringComparison.OrdinalIgnoreCase) ||
            text.StartsWith("/help", StringComparison.OrdinalIgnoreCase))
        {
            await SendMainMenu(chatId, msg.From!, ct);
            return;
        }

        if (text.StartsWith("/add_birthday", StringComparison.OrdinalIgnoreCase))
        {
            await _wizard.TryHandleAsync(new Update
            {
                Message = new Message
                {
                    Chat = new Chat { Id = chatId },
                    From = msg.From,
                    Text = text
                }
            }, ct);
            return;
        }

        if (text.StartsWith("/list", StringComparison.OrdinalIgnoreCase))
        {
            await SendCurrentMonthView(user, chatId, ct);
            return;
        }

        if (text.StartsWith("/remove", StringComparison.OrdinalIgnoreCase))
        {
            var name = text.Replace("/remove", "", StringComparison.OrdinalIgnoreCase).Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                // Show list with delete buttons instead
                await SendAllBirthdaysWithDeleteButtons(user, chatId, ct);
                return;
            }

            var b = await _birthdays.FindByNameAsync(user.Id, name, ct);
            if (b == null)
            {
                await _bot.SendTextMessageAsync(chatId, "Запись не найдена.",
                    replyMarkup: Keyboards.BackToMenuKb, cancellationToken: ct);
                return;
            }

            await _birthdays.DeleteAsync(b.Id, user.Id, ct);
            await _bot.SendTextMessageAsync(chatId,
                "✅ Удалено",
                replyMarkup: Keyboards.BackToMenuKb, cancellationToken: ct);
            return;
        }

        if (text.StartsWith("/settings", StringComparison.OrdinalIgnoreCase))
        {
            await _bot.SendTextMessageAsync(chatId,
                _i18n.GetText(user.Lang, "settings_prompt"),
                parseMode: ParseMode.Html,
                replyMarkup: Keyboards.BackToMenuKb,
                cancellationToken: ct);
            return;
        }

        // Loose settings by free text
        if (await TryApplyLooseSettingsAsync(user, chatId, text, ct))
            return;

        // Fallback — show main menu
        await SendMainMenu(chatId, msg.From!, ct);
    }

    // ════════════════════════════════════════════
    //  Menu callbacks (menu:*)
    // ════════════════════════════════════════════

    private async Task HandleMenuCallbackAsync(Update update, string data, CancellationToken ct)
    {
        var cq = update.CallbackQuery!;
        var chatId = cq.Message!.Chat.Id;
        var user = await EnsureUser(cq.From, ct);

        switch (data)
        {
            case "menu:home":
                await SafeEditMessageAsync(chatId, cq.Message.MessageId,
                    BuildWelcomeText(cq.From),
                    ParseMode.Html, Keyboards.MainMenuKb, ct);
                break;

            case "menu:add":
                // Trigger the wizard via a synthetic /add_birthday message
                await SafeAnswerCallbackQuery(cq.Id, ct: ct);
                await _wizard.TryHandleAsync(new Update
                {
                    Message = new Message
                    {
                        Chat = new Chat { Id = chatId },
                        From = cq.From,
                        Text = "/add_birthday"
                    }
                }, ct);
                return;

            case "menu:list":
                await SafeAnswerCallbackQuery(cq.Id, ct: ct);
                await SendCurrentMonthView(user, chatId, ct);
                return;

            case "menu:settings":
                await SafeEditMessageAsync(chatId, cq.Message.MessageId,
                    _i18n.GetText(user.Lang, "settings_prompt"),
                    null, Keyboards.BackToMenuKb, ct);
                break;

            case "menu:help":
                await SafeEditMessageAsync(chatId, cq.Message.MessageId,
                    _i18n.GetText(user.Lang, "help"),
                    ParseMode.Html, Keyboards.BackToMenuKb, ct);
                break;
        }

        await SafeAnswerCallbackQuery(cq.Id, ct: ct);
    }

    // ════════════════════════════════════════════
    //  Upcoming filter callbacks (up:*)
    // ════════════════════════════════════════════

    private async Task HandleUpcomingCallbackAsync(Update update, string data, CancellationToken ct)
    {
        var cq = update.CallbackQuery!;
        var chatId = cq.Message!.Chat.Id;
        var user = await EnsureUser(cq.From, ct);

        try
        {
            var kind = data[3..]; // after "up:"
            var zone = _tzdb[user.Timezone];
            var today = SystemClock.Instance.GetCurrentInstant().InZone(zone).Date;

            // "all" — show full list with delete buttons
            if (kind == "all")
            {
                await SafeAnswerCallbackQuery(cq.Id, ct: ct);
                await SendAllBirthdaysWithDeleteButtons(user, chatId, ct);
                return;
            }

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

            var items = list
                .Select(b =>
                {
                    var (next, age) = DateHelpers.NextBirthday(today, b.Date);
                    return new UpcomingRow(b.FullName, b.Date, next, age, b.Relation);
                })
                .Where(x => x.NextDate >= from && x.NextDate <= to)
                .OrderBy(x => x.NextDate)
                .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var text = items.Length == 0
                ? "🎉 В выбранный период дней рождения нет."
                : BuildUpcomingHtml(items);

            await SafeEditMessageAsync(chatId, cq.Message.MessageId,
                text, ParseMode.Html, Keyboards.UpcomingKb, ct);

            await SafeAnswerCallbackQuery(cq.Id, ct: ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to handle upcoming filter callback: {Data}", data);
            await SafeAnswerCallbackQuery(cq.Id, "Произошла ошибка. Попробуйте ещё раз.", ct);
        }
    }

    // ════════════════════════════════════════════
    //  List / month navigation callbacks (list:*)
    // ════════════════════════════════════════════

    private async Task HandleListCallbackAsync(Update update, string data, CancellationToken ct)
    {
        var cq = update.CallbackQuery!;
        var chatId = cq.Message!.Chat.Id;
        var user = await EnsureUser(cq.From, ct);

        try
        {
            if (data == "list:all")
            {
                await SafeAnswerCallbackQuery(cq.Id, ct: ct);
                await SendAllBirthdaysWithDeleteButtons(user, chatId, ct);
                return;
            }

            // list:month:YYYY-MM
            if (data.StartsWith("list:month:", StringComparison.Ordinal))
            {
                var parts = data["list:month:".Length..].Split('-');
                if (parts.Length == 2 &&
                    int.TryParse(parts[0], out var year) &&
                    int.TryParse(parts[1], out var month) &&
                    month is >= 1 and <= 12)
                {
                    var (text, kb) = await BuildMonthViewAsync(user, year, month, ct);
                    await SafeEditMessageAsync(chatId, cq.Message.MessageId,
                        text, ParseMode.Html, kb, ct);
                }

                await SafeAnswerCallbackQuery(cq.Id, ct: ct);
                return;
            }

            await SafeAnswerCallbackQuery(cq.Id, ct: ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to handle list callback: {Data}", data);
            await SafeAnswerCallbackQuery(cq.Id, "Произошла ошибка.", ct);
        }
    }

    // ════════════════════════════════════════════
    //  Other callback queries (delete:*)
    // ════════════════════════════════════════════

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

                    if (cq.Message is not null)
                    {
                        await SafeEditMessageAsync(
                            cq.Message.Chat.Id,
                            cq.Message.MessageId,
                            "✅ Запись удалена.",
                            null, Keyboards.BackToMenuKb, ct);
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

        await SafeAnswerCallbackQuery(cq.Id, ct: ct);
    }

    // ════════════════════════════════════════════
    //  View builders
    // ════════════════════════════════════════════

    /// <summary>Sends the main menu with a welcome message.</summary>
    private async Task SendMainMenu(long chatId, Telegram.Bot.Types.User tgUser, CancellationToken ct)
    {
        await _bot.SendTextMessageAsync(chatId,
            BuildWelcomeText(tgUser),
            parseMode: ParseMode.Html,
            replyMarkup: Keyboards.MainMenuKb,
            cancellationToken: ct);
    }

    private static string BuildWelcomeText(Telegram.Bot.Types.User tgUser)
    {
        var name = Formatting.Html(tgUser.FirstName ?? "");
        return
            $"👋 <b>Привет, {name}!</b>\n\n" +
            "Я помогу тебе не забыть ни одного дня рождения.\n" +
            "Выбери действие:";
    }

    /// <summary>Sends the current month view (month navigator + birthday list).</summary>
    private async Task SendCurrentMonthView(BirthdayBot.Domain.Entities.User user, long chatId, CancellationToken ct)
    {
        var zone = _tzdb[user.Timezone];
        var today = SystemClock.Instance.GetCurrentInstant().InZone(zone).Date;
        var (text, kb) = await BuildMonthViewAsync(user, today.Year, today.Month, ct);

        await _bot.SendTextMessageAsync(chatId, text,
            parseMode: ParseMode.Html,
            replyMarkup: kb,
            cancellationToken: ct);
    }

    /// <summary>Builds the month view text and keyboard.</summary>
    private async Task<(string text, InlineKeyboardMarkup kb)> BuildMonthViewAsync(
        BirthdayBot.Domain.Entities.User user, int year, int month, CancellationToken ct)
    {
        var zone = _tzdb[user.Timezone];
        var today = SystemClock.Instance.GetCurrentInstant().InZone(zone).Date;

        var list = await _birthdays.ListByUserAsync(user.Id, ct);

        // Find birthdays whose next occurrence falls in the requested month
        var items = list
            .Select(b =>
            {
                var (next, age) = DateHelpers.NextBirthday(today, b.Date);
                return new UpcomingRow(b.FullName, b.Date, next, age, b.Relation);
            })
            .Where(x => x.NextDate.Year == year && x.NextDate.Month == month)
            .OrderBy(x => x.NextDate.Day)
            .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var sb = new StringBuilder();
        sb.AppendLine($"📅 <b>{InlineCalendarBuilder.GetMonthName(month)} {year}</b>\n");

        if (items.Length == 0)
        {
            sb.AppendLine("В этом месяце дней рождения нет.");
        }
        else
        {
            foreach (var i in items)
            {
                var dayStr = $"{i.NextDate.Day:D2}.{i.NextDate.Month:D2}";
                var relation = string.IsNullOrWhiteSpace(i.Relation) ? "" : $" · {Formatting.Html(i.Relation)}";
                sb.AppendLine($"🎂 <b>{Formatting.Html(i.Name)}</b> — {dayStr}, " +
                              $"{i.Age} {Formatting.PluralYears(i.Age)}{relation}");
            }
        }

        sb.AppendLine($"\n<i>Всего записей: {list.Count}</i>");

        var kb = InlineCalendarBuilder.BuildMonthNavigator(year, month, items.Length > 0);
        return (sb.ToString(), kb);
    }

    /// <summary>Sends all birthdays with per-row delete buttons.</summary>
    private async Task SendAllBirthdaysWithDeleteButtons(BirthdayBot.Domain.Entities.User user, long chatId, CancellationToken ct)
    {
        var list = await _birthdays.ListByUserAsync(user.Id, ct);
        if (list.Count == 0)
        {
            await _bot.SendTextMessageAsync(chatId,
                "📋 Список пуст. Добавьте запись через кнопку ниже.",
                replyMarkup: Keyboards.MainMenuKb,
                cancellationToken: ct);
            return;
        }

        var zone = _tzdb[user.Timezone];
        var today = SystemClock.Instance.GetCurrentInstant().InZone(zone).Date;

        var sb = new StringBuilder();
        sb.AppendLine("📋 <b>Все записи</b>\n");

        var rows = new List<InlineKeyboardButton[]>();

        foreach (var b in list.OrderBy(x => x.Date.Month).ThenBy(x => x.Date.Day))
        {
            var (next, age) = DateHelpers.NextBirthday(today, b.Date);
            var relation = string.IsNullOrWhiteSpace(b.Relation) ? "" : $" · {Formatting.Html(b.Relation)}";
            var interests = string.IsNullOrWhiteSpace(b.Interests) ? "" : $"\n   💡 {Formatting.Html(b.Interests)}";

            sb.AppendLine($"🎂 <b>{Formatting.Html(b.FullName)}</b>");
            sb.AppendLine($"   📅 {b.Date:dd.MM.yyyy} → след. {next:dd.MM}, " +
                          $"{age} {Formatting.PluralYears(age)}{relation}{interests}");

            rows.Add(new[]
            {
                InlineKeyboardButton.WithCallbackData($"🗑 {b.FullName}", $"delete:{b.Id}")
            });
        }

        // Add back-to-menu button at the end
        rows.Add(new[]
        {
            InlineKeyboardButton.WithCallbackData("🏠 Главное меню", "menu:home")
        });

        await _bot.SendTextMessageAsync(chatId, sb.ToString(),
            parseMode: ParseMode.Html,
            replyMarkup: new InlineKeyboardMarkup(rows),
            cancellationToken: ct);
    }

    // ════════════════════════════════════════════
    //  Formatting helpers
    // ════════════════════════════════════════════

    private static string BuildUpcomingHtml(IEnumerable<UpcomingRow> items)
    {
        var sb = new StringBuilder();
        sb.AppendLine("🎉 <b>Ближайшие дни рождения</b>\n");

        foreach (var i in items)
        {
            var dayStr = $"{i.NextDate.Day:D2}.{i.NextDate.Month:D2}";
            var relation = string.IsNullOrWhiteSpace(i.Relation) ? "" : $" · {Formatting.Html(i.Relation)}";
            sb.AppendLine($"🎂 <b>{Formatting.Html(i.Name)}</b> — {dayStr}, " +
                          $"{i.Age} {Formatting.PluralYears(i.Age)}{relation}");
        }

        return sb.ToString();
    }

    private record struct UpcomingRow(string Name, DateOnly BirthDate, LocalDate NextDate, int Age, string? Relation);

    // ════════════════════════════════════════════
    //  Loose settings
    // ════════════════════════════════════════════

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

        if (_tzdb.Ids.Contains(text))
        {
            user.Timezone = text;
            updated = true;
        }

        if (!updated) return false;

        await _users.UpdateAsync(user, ct);
        await _bot.SendTextMessageAsync(chatId, _i18n.GetText(user.Lang, "saved"),
            replyMarkup: Keyboards.BackToMenuKb, cancellationToken: ct);
        return true;
    }

    // ════════════════════════════════════════════
    //  Helpers
    // ════════════════════════════════════════════

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

    /// <summary>
    /// Edit a message, silently ignoring the "message is not modified" Telegram API error.
    /// </summary>
    private async Task SafeEditMessageAsync(
        long chatId, int messageId, string text,
        ParseMode? parseMode, InlineKeyboardMarkup? replyMarkup,
        CancellationToken ct)
    {
        try
        {
            await _bot.EditMessageTextAsync(chatId, messageId, text,
                parseMode: parseMode,
                replyMarkup: replyMarkup,
                cancellationToken: ct);
        }
        catch (ApiRequestException ex) when (ex.Message.Contains("message is not modified", StringComparison.OrdinalIgnoreCase))
        {
            // Telegram returns 400 when text + markup are identical — safe to ignore
            _logger.LogDebug("EditMessageText skipped: message is not modified (chatId={ChatId}, msgId={MsgId})", chatId, messageId);
        }
    }

    private async Task SafeAnswerCallbackQuery(string id, string? text = null, CancellationToken ct = default)
    {
        try { await _bot.AnswerCallbackQueryAsync(id, text, cancellationToken: ct); }
        catch (Exception ex) { _logger.LogDebug(ex, "AnswerCallbackQuery failed"); }
    }

    private static LocalDate FirstDayOfNextMonth(LocalDate d)
    {
        var (y, m) = d.Month == 12 ? (d.Year + 1, 1) : (d.Year, d.Month + 1);
        return new LocalDate(y, m, 1);
    }

    private static LocalDate LastDayOfNextMonth(LocalDate d)
    {
        var first = FirstDayOfNextMonth(d);
        var (y, m) = first.Month == 12 ? (first.Year + 1, 1) : (first.Year, first.Month + 1);
        return new LocalDate(y, m, 1).PlusDays(-1);
    }
}

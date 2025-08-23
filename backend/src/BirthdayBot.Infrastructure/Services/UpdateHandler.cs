using BirthdayBot.Application.Interfaces;
using BirthdayBot.Domain.Entities;
using BirthdayBot.Domain.Enums;
using BirthdayBot.Domain.Utils;
using BirthdayBot.Infrastructure.State;
using MongoDB.Bson;
using NodaTime;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace BirthdayBot.Infrastructure.Services;

public class UpdateHandler : IUpdateHandler
{
    private readonly ITelegramBotClient _bot;
    private readonly IUserRepository _users;
    private readonly IBirthdayRepository _birthdays;
    private readonly ILocalizationService _i18n;
    private readonly InMemoryConversationState _state;
    private readonly IDateTimeZoneProvider _tzdb = DateTimeZoneProviders.Tzdb;

    public UpdateHandler(ITelegramBotClient bot, IUserRepository users, IBirthdayRepository birthdays, ILocalizationService i18n, InMemoryConversationState state)
    {
        _bot = bot;
        _users = users;
        _birthdays = birthdays;
        _i18n = i18n;
        _state = state;
    }

    public async Task HandleUpdateAsync(Update update, CancellationToken ct)
    {
        if (update.Type == UpdateType.Message && update.Message!.Text != null)
        {
            var chatId = update.Message.Chat.Id;
            var text = update.Message.Text.Trim();
            var user = await EnsureUser(update.Message.From!, ct);

            if (text.StartsWith("/start"))
            {
                await _bot.SendTextMessageAsync(chatId, _i18n.GetText(user.Lang, "start"), cancellationToken: ct);
                return;
            }
            if (text.StartsWith("/help"))
            {
                await _bot.SendTextMessageAsync(chatId, _i18n.GetText(user.Lang, "start"), cancellationToken: ct);
                return;
            }
            if (text.StartsWith("/add_birthday"))
            {
                var ctx = _state.Get(user.TelegramUserId);
                ctx.Step = ConversationStep.AddName;
                await _bot.SendTextMessageAsync(chatId, _i18n.GetText(user.Lang, "ask_name"), cancellationToken: ct);
                return;
            }
            if (text.StartsWith("/list"))
            {
                await HandleList(user, chatId, ct);
                return;
            }
            if (text.StartsWith("/remove"))
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
            if (text.StartsWith("/settings"))
            {
                await _bot.SendTextMessageAsync(chatId, _i18n.GetText(user.Lang, "settings_prompt"), cancellationToken: ct);
                return;
            }

            if (text.Contains(':') || text.Contains("ru", StringComparison.OrdinalIgnoreCase) || text.Contains("pl", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("en", StringComparison.OrdinalIgnoreCase) || text.Contains("auto", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("formal", StringComparison.OrdinalIgnoreCase) || text.Contains("friendly", StringComparison.OrdinalIgnoreCase))
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

                if (updated)
                {
                    await _users.UpdateAsync(user, ct);
                    await _bot.SendTextMessageAsync(chatId, _i18n.GetText(user.Lang, "saved"), cancellationToken: ct);
                    return;
                }
            }

            var state = _state.Get(user.TelegramUserId);
            switch (state.Step)
            {
                case ConversationStep.AddName:
                    state.TempName = text;
                    state.Step = ConversationStep.AddDate;
                    await _bot.SendTextMessageAsync(chatId, _i18n.GetText(user.Lang, "ask_date"), cancellationToken: ct);
                    break;
                case ConversationStep.AddDate:
                    if (!DateOnly.TryParseExact(text, "yyyy-MM-dd", out var dob))
                    {
                        await _bot.SendTextMessageAsync(chatId, "Неверный формат даты. Используйте YYYY-MM-DD.", cancellationToken: ct);
                        return;
                    }
                    state.TempDob = dob;
                    state.Step = ConversationStep.AddTimezone;
                    await _bot.SendTextMessageAsync(chatId, _i18n.GetText(user.Lang, "ask_tz"), cancellationToken: ct);
                    break;
                case ConversationStep.AddTimezone:
                    var tz = string.IsNullOrWhiteSpace(text) ? user.Timezone : text.Trim();
                    if (!_tzdb.Ids.Contains(tz))
                    {
                        await _bot.SendTextMessageAsync(chatId, "Неизвестная таймзона. Пример: Europe/Warsaw", cancellationToken: ct);
                        return;
                    }
                    var entity = new Birthday
                    {
                        UserId = user.Id,
                        Name = state.TempName,
                        DateOfBirth = state.TempDob,
                        Tags = new(),
                        Relation = null
                    };
                    await _birthdays.CreateAsync(entity, ct);
                    _state.Clear(user.TelegramUserId);
                    await _bot.SendTextMessageAsync(chatId, _i18n.GetText(user.Lang, "saved"), cancellationToken: ct);
                    break;
                default:
                    await _bot.SendTextMessageAsync(chatId, _i18n.GetText(user.Lang, "start"), cancellationToken: ct);
                    break;
            }
        }
        else if (update.Type == UpdateType.CallbackQuery)
        {
            var cq = update.CallbackQuery!;
            var user = await EnsureUser(cq.From, ct);
            if (cq.Data != null && cq.Data.StartsWith("delete:"))
            {
                var idStr = cq.Data.Substring("delete:".Length);
                if (ObjectId.TryParse(idStr, out var bid))
                {
                    await _birthdays.DeleteAsync(bid, user.Id, ct);
                    await _bot.EditMessageTextAsync(user.TelegramUserId, cq.Message!.MessageId, _i18n.GetText(user.Lang, "removed"), cancellationToken: ct);
                }
            }
            await _bot.AnswerCallbackQueryAsync(cq.Id, cancellationToken: ct);
        }
    }

    private async Task<BirthdayBot.Domain.Entities.User> EnsureUser(Telegram.Bot.Types.User tgUser, CancellationToken ct)
    {
        var existing = await _users.GetByTelegramUserIdAsync(tgUser.Id, ct);
        if (existing != null) return existing;

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

    private async Task HandleList(BirthdayBot.Domain.Entities.User user, long chatId, CancellationToken ct)
    {
        var list = await _birthdays.ListByUserAsync(user.Id, ct);
        if (list.Count == 0)
        {
            await _bot.SendTextMessageAsync(chatId, _i18n.GetText(user.Lang, "list_empty"), cancellationToken: ct);
            return;
        }
        var zone = DateTimeZoneProviders.Tzdb[user.Timezone];
        var today = SystemClock.Instance.GetCurrentInstant().InZone(zone).Date;

        var lines = new List<string>();
        foreach (var b in list)
        {
            var (next, age) = DateHelpers.NextBirthday(today, b.DateOfBirth);
            lines.Add($"{b.Name}: {b.DateOfBirth:yyyy-MM-dd} → next {next:yyyy-MM-dd}, turns {age}");
        }
        var text = string.Join("\n", lines);

        var rows = list.Select(b =>
            new[]
            {
                InlineKeyboardButton.WithCallbackData($"Delete {b.Name}", $"delete:{b.Id}")
            }).ToArray();

        var keyboard = new InlineKeyboardMarkup(rows);
        await _bot.SendTextMessageAsync(chatId, text, replyMarkup: keyboard, cancellationToken: ct);
    }
}
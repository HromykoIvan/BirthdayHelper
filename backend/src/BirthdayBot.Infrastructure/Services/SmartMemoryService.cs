using BirthdayBot.Application.Interfaces;
using BirthdayBot.Application.Utils;
using BirthdayBot.Domain.Entities;
using BirthdayBot.Domain.Enums;
using BirthdayBot.Infrastructure.Mongo;
using MongoDB.Bson;
using MongoDB.Driver;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using PersonOwner = BirthdayBot.Domain.Entities.User;

namespace BirthdayBot.Infrastructure.Services;

/// <summary>
/// Suggests facts from casual text, but never mutates a person's profile until
/// the owner taps an explicit confirmation button. Sessions are scoped and short-lived.
/// </summary>
public sealed class SmartMemoryService
{
    private static readonly TimeSpan SessionTtl = TimeSpan.FromMinutes(10);
    private readonly ITelegramBotClient _bot;
    private readonly IBirthdayRepository _birthdays;
    private readonly IMongoCollection<MemoryFactSessionDocument> _sessions;

    public SmartMemoryService(ITelegramBotClient bot, IBirthdayRepository birthdays, MongoContext context)
    {
        _bot = bot;
        _birthdays = birthdays;
        _sessions = context.MemoryFactSessions;
    }

    private static string T(Language lang, string ru, string pl, string en) =>
        lang switch { Language.Ru => ru, Language.Pl => pl, _ => en };

    public async Task CancelPendingAsync(long chatId, long telegramId, CancellationToken ct)
    {
        await _sessions.DeleteOneAsync(
            x => x.ChatId == chatId && x.TelegramUserId == telegramId, ct);
    }

    public async Task<bool> TryOfferAsync(PersonOwner user, Message msg, CancellationToken ct)
    {
        if (msg.From is null || msg.Text is null ||
            !MemoryFactParser.TryParse(msg.Text, out var fact) || fact is null)
            return false;

        // Never search outside the owner's records. Do not guess between people.
        var records = await _birthdays.ListByUserAsync(user.Id, ct);
        var matches = BirthdayRecipientMatcher.FindMatches(records, fact.Subject);
        var chatId = msg.Chat.Id;

        if (matches.Count == 0)
        {
            await _bot.SendTextMessageAsync(chatId,
                T(user.Lang,
                    $"Не нашёл карточку «{fact.Subject}». Открой «Мои люди», чтобы добавить человека.",
                    $"Nie znalazłem profilu „{fact.Subject}”. Otwórz „Moi bliscy”.",
                    $"I couldn't find “{fact.Subject}”. Open My people to add them."),
                replyMarkup: new InlineKeyboardMarkup(new[]
                {
                    new[] { InlineKeyboardButton.WithCallbackData(
                        T(user.Lang, "👥 Мои люди", "👥 Moi bliscy", "👥 My people"), "menu:people") }
                }), cancellationToken: ct);
            return true;
        }

        if (matches.Count > 8)
        {
            await _bot.SendTextMessageAsync(chatId,
                T(user.Lang, "Несколько похожих людей. Напиши имя точнее, чтобы не перепутать.",
                    "Znalazłem wiele osób. Podaj dokładniejsze imię.",
                    "I found several people. Please use a more specific name."),
                cancellationToken: ct);
            return true;
        }

        // A new proposed fact replaces the previous unconfirmed fact in this chat.
        var doc = new MemoryFactSessionDocument
        {
            ChatId = chatId,
            TelegramUserId = msg.From.Id,
            OwnerId = user.Id,
            CandidateIds = matches.Select(x => x.Id).ToList(),
            SelectedPersonId = matches.Count == 1 ? matches[0].Id : null,
            Field = fact.Field,
            Value = fact.Value,
            ExpiresAtUtc = DateTime.UtcNow.Add(SessionTtl)
        };
        await _sessions.ReplaceOneAsync(x => x.ChatId == chatId, doc,
            new ReplaceOptions { IsUpsert = true }, ct);

        if (matches.Count > 1)
        {
            var options = matches
                .Select(x => new[]
                {
                    InlineKeyboardButton.WithCallbackData(
                        $"👤 {Trim(x.FullName, 35)} · {x.Date:dd.MM}",
                        $"memory:pick:{x.Id}")
                })
                .ToList();
            options.Add(new[] { InlineKeyboardButton.WithCallbackData(
                T(user.Lang, "Не сохранять", "Nie zapisuj", "Don't save"), "memory:cancel") });

            await _bot.SendTextMessageAsync(chatId,
                T(user.Lang,
                    $"О ком ты говоришь? Нашёл {matches.Count} похожих карточки.",
                    $"O kogo chodzi? Znalazłem {matches.Count} pasujących profili.",
                    $"Which person? I found {matches.Count} matching profiles."),
                replyMarkup: new InlineKeyboardMarkup(options),
                cancellationToken: ct);
        }
        else
        {
            await OfferConfirmationAsync(user, chatId, matches[0], fact.Field, fact.Value, ct);
        }

        return true;
    }

    public async Task HandleCallbackAsync(PersonOwner user, CallbackQuery callback, CancellationToken ct)
    {
        var chatId = callback.Message?.Chat.Id ?? callback.From.Id;
        var data = callback.Data ?? "";

        if (data == "memory:cancel")
        {
            await CancelPendingAsync(chatId, callback.From.Id, ct);
            await _bot.AnswerCallbackQueryAsync(callback.Id, cancellationToken: ct);
            await _bot.SendTextMessageAsync(chatId,
                T(user.Lang, "Хорошо, не сохраняю.", "Dobrze, nie zapisuję.", "Okay, I won't save it."),
                cancellationToken: ct);
            return;
        }

        var filter = Builders<MemoryFactSessionDocument>.Filter.Where(s =>
            s.ChatId == chatId && s.TelegramUserId == callback.From.Id &&
            s.OwnerId == user.Id && s.ExpiresAtUtc > DateTime.UtcNow);
        if (data.StartsWith("memory:pick:", StringComparison.Ordinal) &&
            ObjectId.TryParse(data["memory:pick:".Length..], out var chosenId))
        {
            var withCandidate = filter & Builders<MemoryFactSessionDocument>.Filter
                .AnyEq(x => x.CandidateIds, chosenId);
            var updated = await _sessions.FindOneAndUpdateAsync(
                withCandidate,
                Builders<MemoryFactSessionDocument>.Update.Set(x => x.SelectedPersonId, chosenId),
                cancellationToken: ct);

            if (updated is null)
            {
                await ExpiredAsync(user, callback, chatId, ct);
                return;
            }

            var person = await _birthdays.GetByIdAsync(chosenId, user.Id, ct);
            if (person is null)
            {
                await CancelPendingAsync(chatId, callback.From.Id, ct);
                await ExpiredAsync(user, callback, chatId, ct);
                return;
            }

            await _bot.AnswerCallbackQueryAsync(callback.Id, cancellationToken: ct);
            await OfferConfirmationAsync(user, chatId, person, updated.Field, updated.Value, ct);
            return;
        }

        if (data == "memory:save")
        {
            // Atomic consume guarantees a double tap never appends the same fact twice.
            var selected = filter &
                Builders<MemoryFactSessionDocument>.Filter.Ne(x => x.SelectedPersonId, null);
            var proposed = await _sessions.FindOneAndDeleteAsync(selected, cancellationToken: ct);
            if (proposed is null ||
                !proposed.SelectedPersonId.HasValue ||
                !proposed.CandidateIds.Contains(proposed.SelectedPersonId.Value))
            {
                await ExpiredAsync(user, callback, chatId, ct);
                return;
            }

            var person = await _birthdays.GetByIdAsync(proposed.SelectedPersonId.Value, user.Id, ct);
            if (person is null)
            {
                await ExpiredAsync(user, callback, chatId, ct);
                return;
            }

            if (!PersonProfileEditor.TryApply(person, proposed.Field, proposed.Value, out var error))
            {
                await _bot.AnswerCallbackQueryAsync(callback.Id, cancellationToken: ct);
                await _bot.SendTextMessageAsync(chatId,
                    T(user.Lang,
                        "Не удалось сохранить: слишком много заметок. Открой карточку и отредактируй их.",
                        "Nie udało się zapisać. Sprawdź notatki w profilu.",
                        "Couldn't save that fact. Please review the notes in the profile."),
                    cancellationToken: ct);
                return;
            }

            await _birthdays.UpdateAsync(person, ct);
            await _bot.AnswerCallbackQueryAsync(callback.Id, cancellationToken: ct);
            await _bot.SendTextMessageAsync(chatId,
                T(user.Lang, $"✅ Запомнил про {person.FullName}.",
                    $"✅ Zapamiętałem informację o {person.FullName}.",
                    $"✅ Saved for {person.FullName}."),
                replyMarkup: new InlineKeyboardMarkup(new[]
                {
                    new[] { InlineKeyboardButton.WithCallbackData(
                        T(user.Lang, "👤 Открыть карточку", "👤 Otwórz profil", "👤 Open profile"),
                        $"person:show:{person.Id}") }
                }), cancellationToken: ct);
            return;
        }

        await _bot.AnswerCallbackQueryAsync(callback.Id, cancellationToken: ct);
    }

    private async Task OfferConfirmationAsync(PersonOwner user, long chatId, Birthday person,
        string field, string value, CancellationToken ct)
    {
        var category = field switch
        {
            "append_interests" => T(user.Lang, "Интересы", "Zainteresowania", "Interests"),
            "append_gifts" => T(user.Lang, "Подарки и пожелания", "Prezenty i życzenia", "Gift preferences"),
            _ => T(user.Lang, "Заметки", "Notatki", "Notes")
        };

        var message = T(user.Lang,
            $"Запомнить про <b>{Formatting.Html(person.FullName)}</b>?\n<b>{category}:</b> {Formatting.Html(value)}",
            $"Zapisać dla <b>{Formatting.Html(person.FullName)}</b>?\n<b>{category}:</b> {Formatting.Html(value)}",
            $"Remember this about <b>{Formatting.Html(person.FullName)}</b>?\n<b>{category}:</b> {Formatting.Html(value)}");

        await _bot.SendTextMessageAsync(chatId, message, parseMode: ParseMode.Html,
            replyMarkup: new InlineKeyboardMarkup(new[]
            {
                new[] {
                    InlineKeyboardButton.WithCallbackData(
                        T(user.Lang, "✅ Запомнить", "✅ Zapisz", "✅ Save"), "memory:save"),
                    InlineKeyboardButton.WithCallbackData(
                        T(user.Lang, "❌ Не надо", "❌ Nie", "❌ No thanks"), "memory:cancel")
                }
            }), cancellationToken: ct);
    }

    private async Task ExpiredAsync(PersonOwner user, CallbackQuery callback, long chatId, CancellationToken ct)
    {
        await _bot.AnswerCallbackQueryAsync(callback.Id, cancellationToken: ct);
        await _bot.SendTextMessageAsync(chatId,
            T(user.Lang, "Предложение устарело. Напиши факт ещё раз, если хочешь его сохранить.",
                "Ta propozycja wygasła. Wyślij informację ponownie.",
                "This suggestion expired. Send the detail again if you'd like to save it."),
            cancellationToken: ct);
    }

    private static string Trim(string text, int length) =>
        text.Length <= length ? text : text[..length] + "…";
}

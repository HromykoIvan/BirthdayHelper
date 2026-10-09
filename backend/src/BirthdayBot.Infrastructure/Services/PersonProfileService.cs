using BirthdayBot.Application.Interfaces;
using BirthdayBot.Application.Utils;
using BirthdayBot.Domain.Entities;
using PersonOwner = BirthdayBot.Domain.Entities.User;
using BirthdayBot.Domain.Enums;
using BirthdayBot.Infrastructure.Mongo;
using MongoDB.Bson;
using MongoDB.Driver;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace BirthdayBot.Infrastructure.Services;

/// <summary>
/// Telegram-first people directory. Changes are always explicit, scoped to the Telegram user,
/// and stored with a short-lived session in Mongo so Cloud Run restarts are safe.
/// </summary>
public sealed class PersonProfileService
{
    private const int PageSize = 8;
    private static readonly TimeSpan EditTtl = TimeSpan.FromMinutes(20);
    private readonly ITelegramBotClient _bot;
    private readonly IBirthdayRepository _birthdays;
    private readonly IMongoCollection<PersonProfileSessionDocument> _sessions;

    public PersonProfileService(ITelegramBotClient bot, IBirthdayRepository birthdays, MongoContext context)
    {
        _bot = bot;
        _birthdays = birthdays;
        _sessions = context.PersonProfileSessions;
    }

    public async Task CancelPendingAsync(long chatId, long telegramUserId, CancellationToken ct)
    {
        await _sessions.DeleteOneAsync(x => x.ChatId == chatId &&
            x.TelegramUserId == telegramUserId, ct);
    }

    private static string L(Language lang, string ru, string pl, string en) =>
        lang switch { Language.Ru => ru, Language.Pl => pl, _ => en };

    public async Task ShowPeopleAsync(PersonOwner user, long chatId, int page, int? messageId, CancellationToken ct)
    {
        var entries = (await _birthdays.ListByUserAsync(user.Id, ct))
            .OrderBy(x => x.FullName, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();

        if (entries.Length == 0)
        {
            await SendOrEditAsync(chatId, messageId,
                L(user.Lang,
                    "👥 <b>Мои люди</b>\n\nПока никого нет. Напиши, например: «Добавь Анну, 12 июня» — и я запомню.",
                    "👥 <b>Moi bliscy</b>\n\nNa razie nikogo tu nie ma. Napisz: „Dodaj Annę, 12 czerwca”.",
                    "👥 <b>My people</b>\n\nNo one here yet. Try: “Add Anna, June 12”."),
                new InlineKeyboardMarkup(new[]
                {
                    new[] { InlineKeyboardButton.WithCallbackData(
                        L(user.Lang, "➕ Добавить", "➕ Dodaj", "➕ Add"), "menu:add") },
                    new[] { InlineKeyboardButton.WithCallbackData(
                        L(user.Lang, "⬅️ Главная", "⬅️ Menu", "⬅️ Home"), "menu:home") }
                }), ct);
            return;
        }

        var pages = (entries.Length + PageSize - 1) / PageSize;
        page = Math.Clamp(page, 0, pages - 1);
        var text = L(user.Lang,
            $"👥 <b>Мои люди</b> · {entries.Length}\n\nНажми на имя, чтобы увидеть карточку, дополнить интересы или запомнить важное.",
            $"👥 <b>Moi bliscy</b> · {entries.Length}\n\nWybierz osobę, aby zobaczyć lub uzupełnić profil.",
            $"👥 <b>My people</b> · {entries.Length}\n\nChoose a person to open or update their profile.");

        var rows = entries.Skip(page * PageSize).Take(PageSize)
            .Select(p => new[]
            {
                InlineKeyboardButton.WithCallbackData(
                    $"🎂 {Trim(p.FullName, 28)} · {p.Date:dd.MM}", $"person:show:{p.Id}")
            })
            .ToList();

        if (pages > 1)
        {
            var nav = new List<InlineKeyboardButton>();
            if (page > 0) nav.Add(InlineKeyboardButton.WithCallbackData("◀", $"person:list:{page - 1}"));
            nav.Add(InlineKeyboardButton.WithCallbackData($"{page + 1}/{pages}", "person:noop"));
            if (page + 1 < pages) nav.Add(InlineKeyboardButton.WithCallbackData("▶", $"person:list:{page + 1}"));
            rows.Add(nav.ToArray());
        }

        rows.Add(new[] { InlineKeyboardButton.WithCallbackData(
            L(user.Lang, "➕ Добавить", "➕ Dodaj", "➕ Add"), "menu:add") });
        rows.Add(new[] { InlineKeyboardButton.WithCallbackData(
            L(user.Lang, "⬅️ Главная", "⬅️ Menu", "⬅️ Home"), "menu:home") });
        await SendOrEditAsync(chatId, messageId, text, new InlineKeyboardMarkup(rows), ct);
    }

    public async Task HandleCallbackAsync(PersonOwner user, CallbackQuery callback, CancellationToken ct)
    {
        var data = callback.Data ?? "";
        var chatId = callback.Message?.Chat.Id ?? callback.From.Id;
        var messageId = callback.Message?.MessageId;

        if (data == "person:noop")
        {
            await _bot.AnswerCallbackQueryAsync(callback.Id, cancellationToken: ct);
            return;
        }

        if (data == "person:cancel")
        {
            await _sessions.DeleteOneAsync(
                x => x.ChatId == chatId && x.TelegramUserId == callback.From.Id, ct);
            await _bot.AnswerCallbackQueryAsync(callback.Id, cancellationToken: ct);
            await ShowPeopleAsync(user, chatId, 0, messageId, ct);
            return;
        }

        if (data.StartsWith("person:list:", StringComparison.Ordinal))
        {
            await CancelPendingAsync(chatId, callback.From.Id, ct);
            var page = int.TryParse(data["person:list:".Length..], out var p) ? p : 0;
            await _bot.AnswerCallbackQueryAsync(callback.Id, cancellationToken: ct);
            await ShowPeopleAsync(user, chatId, page, messageId, ct);
            return;
        }

        var parts = data.Split(':', StringSplitOptions.None);
        var action = parts.Length > 1 ? parts[1] : "";
        var idValue = parts.Length == 3 && action == "show" ? parts[2]
            : parts.Length == 4 && action == "edit" ? parts[3] : "";
        if (!ObjectId.TryParse(idValue, out var id))
        {
            await _bot.AnswerCallbackQueryAsync(callback.Id, cancellationToken: ct);
            return;
        }

        // Critical: never load a record without its Mongo user scope.
        var person = await _birthdays.GetByIdAsync(id, user.Id, ct);
        if (person is null)
        {
            await _bot.AnswerCallbackQueryAsync(callback.Id,
                L(user.Lang, "Карточка не найдена", "Nie znaleziono profilu", "Profile not found"),
                cancellationToken: ct);
            return;
        }

        if (action == "show")
        {
            await CancelPendingAsync(chatId, callback.From.Id, ct);
            await _bot.AnswerCallbackQueryAsync(callback.Id, cancellationToken: ct);
            await ShowPersonAsync(user, person, chatId, messageId, ct);
            return;
        }

        if (action == "edit")
        {
            var field = parts[2];
            if (!PersonProfileEditor.Fields.Contains(field))
            {
                await _bot.AnswerCallbackQueryAsync(callback.Id, cancellationToken: ct);
                return;
            }

            await _sessions.ReplaceOneAsync(x => x.ChatId == chatId,
                new PersonProfileSessionDocument
                {
                    ChatId = chatId,
                    TelegramUserId = callback.From.Id,
                    BirthdayId = person.Id,
                    Field = field,
                    ExpiresAtUtc = DateTime.UtcNow.Add(EditTtl)
                }, new ReplaceOptions { IsUpsert = true }, ct);

            await _bot.AnswerCallbackQueryAsync(callback.Id, cancellationToken: ct);
            await _bot.SendTextMessageAsync(chatId, Prompt(user.Lang, person.FullName, field),
                parseMode: ParseMode.Html,
                replyMarkup: new InlineKeyboardMarkup(new[]
                {
                    new[] { InlineKeyboardButton.WithCallbackData(
                        L(user.Lang, "❌ Отмена", "❌ Anuluj", "❌ Cancel"), "person:cancel") }
                }), cancellationToken: ct);
        }
    }

    public async Task<bool> TryHandleTextAsync(PersonOwner user, Message message, CancellationToken ct)
    {
        if (message.From is null || string.IsNullOrWhiteSpace(message.Text))
            return false;

        var text = message.Text.Trim();
        var chatId = message.Chat.Id;
        var session = await _sessions.Find(x => x.ChatId == chatId &&
            x.TelegramUserId == message.From.Id && x.ExpiresAtUtc > DateTime.UtcNow)
            .FirstOrDefaultAsync(ct);
        if (session is null)
            return false;

        // Commands must continue to work even while an edit is pending.
        if (text.StartsWith("/", StringComparison.Ordinal) && text != "/cancel")
            return false;

        if (text.Equals("/cancel", StringComparison.OrdinalIgnoreCase) ||
            text.Equals("отмена", StringComparison.OrdinalIgnoreCase) ||
            text.Equals("anuluj", StringComparison.OrdinalIgnoreCase) ||
            text.Equals("cancel", StringComparison.OrdinalIgnoreCase))
        {
            await _sessions.DeleteOneAsync(x => x.ChatId == chatId &&
                x.TelegramUserId == message.From.Id, ct);
            await _bot.SendTextMessageAsync(chatId,
                L(user.Lang, "Изменение отменено.", "Anulowano zmianę.", "Edit cancelled."),
                cancellationToken: ct);
            return true;
        }

        var person = await _birthdays.GetByIdAsync(session.BirthdayId, user.Id, ct);
        if (person is null)
        {
            await _sessions.DeleteOneAsync(x => x.ChatId == chatId &&
                x.TelegramUserId == message.From.Id, ct);
            return false;
        }

        if (!PersonProfileEditor.TryApply(person, session.Field, text, out var error))
        {
            var prompt = error switch
            {
                "invalid_date" => L(user.Lang,
                    "Не получилось распознать дату. Напиши 12.06 или 12.06.1990.",
                    "Nie rozumiem daty. Wpisz 12.06 lub 12.06.1990.",
                    "I couldn't read the date. Try 12.06 or 12.06.1990."),
                "notes_full" => L(user.Lang,
                    "Заметок уже много. Открой карточку и отредактируй их.",
                    "Za dużo notatek. Edytuj je w profilu.",
                    "Notes are full. Please edit the existing notes."),
                _ => L(user.Lang,
                    "Слишком длинный или пустой текст. Попробуй короче, либо нажми «Отмена».",
                    "Tekst jest pusty lub za długi. Spróbuj krócej.",
                    "Text is empty or too long. Try something shorter.")
            };
            await _bot.SendTextMessageAsync(chatId, prompt, cancellationToken: ct);
            return true;
        }

        await _birthdays.UpdateAsync(person, ct);
        await _sessions.DeleteOneAsync(x => x.ChatId == chatId &&
            x.TelegramUserId == message.From.Id, ct);
        await _bot.SendTextMessageAsync(chatId,
            L(user.Lang, "✅ Запомнил!", "✅ Zapamiętane!", "✅ Saved!"),
            cancellationToken: ct);
        await ShowPersonAsync(user, person, chatId, null, ct);
        return true;
    }

    private async Task ShowPersonAsync(PersonOwner user, Birthday person, long chatId, int? messageId, CancellationToken ct)
    {
        var lang = user.Lang;
        var birthday = person.HasKnownBirthYear
            ? person.Date.ToString("dd.MM.yyyy")
            : person.Date.ToString("dd.MM");
        // Show what is known, not a long questionnaire full of empty fields.
        var builder = new System.Text.StringBuilder();
        builder.AppendLine($"👤 <b>{Formatting.Html(person.FullName)}</b>");
        builder.AppendLine($"🎂 {L(lang, "День рождения", "Urodziny", "Birthday")}: {birthday}");
        var facts = 0;
        void AddFact(string? value, string label)
        {
            if (string.IsNullOrWhiteSpace(value))
                return;
            builder.AppendLine($"{label}: {Formatting.Html(Trim(value, 1200))}");
            facts++;
        }

        AddFact(person.Relation, L(lang, "👥 Кто это", "👥 Relacja", "👥 Relationship"));
        AddFact(person.Interests, L(lang, "💡 Интересы", "💡 Zainteresowania", "💡 Interests"));
        AddFact(person.Profession, L(lang, "💼 Занятие", "💼 Zawód", "💼 Job"));
        AddFact(person.GiftIdeas, L(lang, "🎁 Идеи подарков", "🎁 Pomysły na prezent", "🎁 Gift ideas"));
        AddFact(person.Notes, L(lang, "📝 Заметки", "📝 Notatki", "📝 Notes"));
        builder.AppendLine();
        builder.AppendLine(facts == 0
            ? L(lang, "Пока помню только дату. Можно добавить одну деталь — это необязательно.",
                      "Na razie pamiętam datę. Możesz dodać szczegóły później.",
                      "I only know the birthday so far. Feel free to add details later.")
            : L(lang, "Можешь дополнить карточку в любое время.",
                      "Możesz uzupełnić profil w dowolnej chwili.",
                      "You can add more details at any time."));
        var text = builder.ToString();

        InlineKeyboardButton Edit(string label, string field) =>
            InlineKeyboardButton.WithCallbackData(label, $"person:edit:{field}:{person.Id}");

        var kb = new InlineKeyboardMarkup(new[]
        {
            new[] { Edit(L(lang, "✏️ Имя", "✏️ Imię", "✏️ Name"), "name"),
                    Edit(L(lang, "📅 Дата", "📅 Data", "📅 Date"), "date") },
            new[] { Edit(L(lang, "👥 Отношения", "👥 Relacja", "👥 Relationship"), "relation"),
                    Edit(L(lang, "💡 Интересы", "💡 Zainteresowania", "💡 Interests"), "interests") },
            new[] { Edit(L(lang, "💼 Занятие", "💼 Zawód", "💼 Job"), "profession"),
                    Edit(L(lang, "🎁 Подарки", "🎁 Prezenty", "🎁 Gifts"), "gifts") },
            new[] { Edit(L(lang, "📝 Заметки", "📝 Notatki", "📝 Notes"), "notes") },
            new[] { Edit(L(lang, "➕ Запомнить факт", "➕ Zapamiętaj fakt", "➕ Remember a detail"), "memory") },
            new[] { InlineKeyboardButton.WithCallbackData(
                L(lang, "⬅️ Мои люди", "⬅️ Moi bliscy", "⬅️ My people"), "person:list:0") }
        });

        await SendOrEditAsync(chatId, messageId, text, kb, ct);
    }

    private static string Prompt(Language lang, string name, string field)
    {
        var safeName = Formatting.Html(name);
        var heading = field switch
        {
            "name" => L(lang, "имя и фамилию", "imię i nazwisko", "name"),
            "date" => L(lang, "дату (например, 12.06 или 12.06.1990)",
                             "datę (np. 12.06 lub 12.06.1990)", "date (e.g. 12.06 or 12.06.1990)"),
            "relation" => L(lang, "кем тебе приходится человек", "relację", "relationship"),
            "interests" => L(lang, "интересы", "zainteresowania", "interests"),
            "profession" => L(lang, "чем занимается", "zawód", "profession"),
            "gifts" => L(lang, "идеи и предпочтения для подарков", "pomysły na prezenty", "gift ideas"),
            "notes" => L(lang, "заметки", "notatki", "notes"),
            _ => L(lang, "одну вещь, которую стоит запомнить", "jedną rzecz do zapamiętania",
                             "one thing you'd like to remember")
        };
        return L(lang,
            $"<b>{safeName}</b>\n\nНапиши {heading}.\n<i>Для очистки поля отправь «—». Ничего не сохраняю, пока ты не ответишь.</i>",
            $"<b>{safeName}</b>\n\nPodaj {heading}.\n<i>Wyślij „—”, aby wyczyścić pole.</i>",
            $"<b>{safeName}</b>\n\nSend {heading}.\n<i>Send “—” to clear an optional field.</i>");
    }

    private static string Trim(string value, int max) =>
        value.Length <= max ? value : value[..max] + "…";

    private async Task SendOrEditAsync(long chatId, int? messageId, string text,
        InlineKeyboardMarkup kb, CancellationToken ct)
    {
        if (messageId.HasValue)
        {
            try
            {
                await _bot.EditMessageTextAsync(chatId, messageId.Value, text,
                    parseMode: ParseMode.Html, replyMarkup: kb, cancellationToken: ct);
                return;
            }
            catch (ApiRequestException ex) when (ex.Message.Contains(
                "message is not modified", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
            catch (ApiRequestException)
            {
                // Telegram may not allow editing older messages. Send a new card.
            }
        }

        await _bot.SendTextMessageAsync(chatId, text, parseMode: ParseMode.Html,
            replyMarkup: kb, cancellationToken: ct);
    }
}

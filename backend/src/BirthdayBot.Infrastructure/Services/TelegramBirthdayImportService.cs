using System.Globalization;
using System.Text;
using BirthdayBot.Application.Services;
using BirthdayBot.Application.UI;
using BirthdayBot.Domain.Entities;
using User = BirthdayBot.Domain.Entities.User;
using BirthdayBot.Domain.Enums;
using BirthdayBot.Infrastructure.Mongo;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using BirthdayBot.Infrastructure.Options;
using MongoDB.Driver;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace BirthdayBot.Infrastructure.Services;

/// <summary>
/// Upload -> preview -> explicit confirmation. Only name and birthday are persisted.
/// No social account credentials or contact phone/email data are stored.
/// </summary>
public sealed class TelegramBirthdayImportService
{
    private const int PageSize = 8;
    private readonly IMongoCollection<BirthdayImportSessionDocument> _sessions;
    private readonly MongoContext _database;
    private readonly ITelegramBotClient _bot;
    private readonly ILogger<TelegramBirthdayImportService> _logger;
    private readonly VkImportOptions _vkOptions;

    public TelegramBirthdayImportService(
        MongoContext database,
        ITelegramBotClient bot,
        IOptions<VkImportOptions> vkOptions,
        ILogger<TelegramBirthdayImportService> logger)
    {
        _sessions = database.ImportSessions;
        _database = database;
        _bot = bot;
        _logger = logger;
        _vkOptions = vkOptions.Value;
    }

    public async Task ShowInstructionsAsync(User user, long chatId, CancellationToken ct)
    {
        var message = user.Lang switch
        {
            Language.Ru =>
                "📥 <b>Импорт дней рождения</b>\n\n" +
                "Отправь сюда файл <b>.vcf</b> или <b>.csv</b> с контактами.\n" +
                "• iPhone/iCloud: экспорт контактов как vCard (.vcf).\n" +
                "• Google Contacts: «Экспорт» → Google CSV или vCard.\n" +
                "• Android: Контакты → Экспорт → .vcf.\n\n" +
                "Я покажу только контакты с датой рождения, найду совпадения и попрошу подтвердить импорт. " +
                "Телефоны, адреса и email не сохраняются. Файл до 2 МБ; до 1000 дат за раз.",
            Language.Pl =>
                "📥 <b>Import urodzin</b>\n\n" +
                "Wyślij plik <b>.vcf</b> lub <b>.csv</b>. iCloud/Android: eksport vCard. " +
                "Google Contacts: Eksport → Google CSV lub vCard.\n\n" +
                "Pokażę kontakty z datą urodzin i poproszę o potwierdzenie. " +
                "Nie zapisuję numerów telefonów, emaili ani adresów. Maksymalnie 2 MB i 1000 dat.",
            _ =>
                "📥 <b>Import birthdays</b>\n\n" +
                "Send a <b>.vcf</b> or <b>.csv</b> contact export. " +
                "Export vCard from iCloud/Android, or Google CSV/vCard from Google Contacts.\n\n" +
                "I'll preview birthdays and ask for confirmation. " +
                "Phone numbers, emails and addresses are not saved. Max 2 MB, 1000 birthdays."
        };

        var rows = new List<InlineKeyboardButton[]>();
        if (_vkOptions.IsConfigured)
        {
            rows.Add(new[]
            {
                InlineKeyboardButton.WithCallbackData("🔗 ВКонтакте / VK", "vk:connect")
            });
        }

        rows.Add(new[]
        {
            InlineKeyboardButton.WithCallbackData("🏠 Главное меню", "menu:home")
        });

        await _bot.SendTextMessageAsync(
            chatId, message,
            parseMode: ParseMode.Html,
            replyMarkup: new InlineKeyboardMarkup(rows),
            cancellationToken: ct);
    }

    public async Task HandleDocumentAsync(
        User user,
        long chatId,
        long senderTelegramId,
        Document document,
        CancellationToken ct)
    {
        if (chatId != senderTelegramId)
        {
            await _bot.SendTextMessageAsync(chatId,
                "For privacy, import contact files only in a private chat with the bot.",
                cancellationToken: ct);
            return;
        }

        var filename = document.FileName ?? "";
        var extension = Path.GetExtension(filename).ToLowerInvariant();
        if (extension is not (".vcf" or ".csv"))
        {
            await ShowInstructionsAsync(user, chatId, ct);
            return;
        }

        if (document.FileSize is > ContactImportParser.MaxFileBytes)
        {
            await _bot.SendTextMessageAsync(chatId,
                "This file is too large. Export a smaller batch (max 2 MB).",
                cancellationToken: ct);
            return;
        }

        try
        {
            var file = await _bot.GetFileAsync(document.FileId, ct);
            if (string.IsNullOrWhiteSpace(file.FilePath))
                throw new InvalidOperationException("Telegram did not provide a downloadable file.");

            using var bytes = new MemoryStream();
            await _bot.DownloadFileAsync(file.FilePath, bytes, ct);
            if (bytes.Length > ContactImportParser.MaxFileBytes)
                throw new ArgumentException("The file is larger than 2 MB.");

            // Reject non-UTF8 rather than silently corrupting imported names.
            var utf8 = new UTF8Encoding(false, true);
            var content = utf8.GetString(bytes.ToArray());
            var parsed = ContactImportParser.Parse(filename, content);

            await StartPreviewAsync(user, chatId, extension, parsed, ct);
        }
        catch (DecoderFallbackException)
        {
            await _bot.SendTextMessageAsync(chatId,
                "The file must be UTF-8. Please export contacts as UTF-8 vCard or CSV.",
                cancellationToken: ct);
        }
        catch (ArgumentException ex)
        {
            // Only expose safe parser validation errors; don't include file contents.
            _logger.LogInformation("Contact import rejected: {Reason}", ex.Message);
            await _bot.SendTextMessageAsync(chatId,
                $"Couldn't read this export: {ex.Message}",
                cancellationToken: ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Contact import preview failed (chat {ChatId})", chatId);
            await _bot.SendTextMessageAsync(chatId,
                "Could not open this contact export. Try exporting it as Google CSV or vCard.",
                cancellationToken: ct);
        }
    }

    /// <summary>
    /// Common preview used by both file exports and VK ID. Nothing is saved until explicit confirmation.
    /// Only name, day/month and optional birth year enter the Mongo import session.
    /// </summary>
    public async Task StartPreviewAsync(
        User user,
        long chatId,
        string source,
        ContactImportParser.Result parsed,
        CancellationToken ct)
    {
        if (parsed.Entries.Count == 0)
        {
            await _bot.SendTextMessageAsync(
                chatId,
                user.Lang switch
                {
                    Language.Ru => $"В файле нет подходящих дней рождения. Без даты: {parsed.WithoutBirthday}, некорректных: {parsed.Invalid}.",
                    Language.Pl => "W pliku nie znaleziono poprawnych dat urodzin.",
                    _ => "No valid birthdays were found in the file."
                },
                cancellationToken: ct);
            return;
        }

        var existing = await _database.Birthdays
            .Find(b => b.UserId == user.Id)
            .ToListAsync(ct);

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var datesByName = new Dictionary<string, string>(StringComparer.Ordinal);
        var candidates = new List<BirthdayImportCandidate>();
        foreach (var entry in parsed.Entries)
        {
            var normalizedName = Normalize(entry.FullName);
            var dateStamp = $"{entry.Birthday.Month:D2}{entry.Birthday.Day:D2}";
            var fingerprint = $"{normalizedName}|{dateStamp}";
            var nameMatches = existing.Where(b =>
                Normalize(b.FullName) == normalizedName).ToArray();

            var fileNameConflict = datesByName.TryGetValue(normalizedName, out var priorDate) &&
                                   priorDate != dateStamp;
            datesByName.TryAdd(normalizedName, dateStamp);
            var repeatedInFile = !seen.Add(fingerprint);
            var exactMatch = repeatedInFile || nameMatches.Any(b =>
                b.Date.Month == entry.Birthday.Month &&
                b.Date.Day == entry.Birthday.Day);
            var status = exactMatch ? "Duplicate"
                : nameMatches.Length > 0 || fileNameConflict ? "Conflict"
                : "New";

            candidates.Add(new BirthdayImportCandidate
            {
                Index = candidates.Count,
                FirstName = entry.FirstName,
                LastName = entry.LastName,
                Date = entry.Birthday,
                YearKnown = entry.YearKnown,
                Status = status,
                Selected = status == "New"
            });
        }

        var session = new BirthdayImportSessionDocument
        {
            ChatId = chatId,
            UserId = user.Id,
            Source = source,
            Candidates = candidates,
            WithoutBirthday = parsed.WithoutBirthday,
            Invalid = parsed.Invalid,
            ExpiresAtUtc = DateTime.UtcNow.AddMinutes(60)
        };
        await _sessions.ReplaceOneAsync(
            x => x.ChatId == chatId,
            session,
            new ReplaceOptions { IsUpsert = true },
            ct);

        await SendPreviewAsync(user, chatId, session, 0, messageId: null, ct);

    }

    public async Task HandleCallbackAsync(
        User user,
        CallbackQuery callback,
        CancellationToken ct)
    {
        var chatId = callback.Message?.Chat.Id ?? 0;
        if (chatId == 0 || chatId != callback.From.Id)
        {
            await _bot.AnswerCallbackQueryAsync(callback.Id, cancellationToken: ct);
            return;
        }

        var data = callback.Data ?? "";
        var session = await _sessions.Find(x =>
            x.ChatId == chatId && x.UserId == user.Id &&
            x.ExpiresAtUtc > DateTime.UtcNow).FirstOrDefaultAsync(ct);

        if (session is null)
        {
            await _bot.AnswerCallbackQueryAsync(callback.Id,
                "Import preview expired. Please upload your file again.",
                cancellationToken: ct);
            return;
        }

        if (session.Status != "Preview")
        {
            await _bot.AnswerCallbackQueryAsync(callback.Id,
                "This import is already being processed.",
                cancellationToken: ct);
            return;
        }

        if (data == "import:cancel")
        {
            await _sessions.DeleteOneAsync(x => x.ChatId == chatId && x.UserId == user.Id, ct);
            await _bot.AnswerCallbackQueryAsync(callback.Id, cancellationToken: ct);
            await _bot.EditMessageTextAsync(
                chatId, callback.Message!.MessageId,
                user.Lang == Language.Ru ? "Импорт отменён." : "Import cancelled.",
                cancellationToken: ct);
            return;
        }

        if (data == "import:save")
        {
            await _bot.AnswerCallbackQueryAsync(callback.Id, cancellationToken: ct);
            await CommitAsync(user, chatId, callback.Message!.MessageId, ct);
            return;
        }

        var parts = data.Split(':');
        if (parts.Length == 3 && int.TryParse(parts[2], out var number))
        {
            if (parts[1] == "toggle" && number >= 0 && number < session.Candidates.Count)
            {
                var row = session.Candidates[number];
                if (row.Status != "Duplicate")
                {
                    row.Selected = !row.Selected;
                    await _sessions.ReplaceOneAsync(
                        x => x.ChatId == chatId && x.UserId == user.Id && x.Status == "Preview",
                        session, cancellationToken: ct);
                }
                number /= PageSize;
            }
            else if (parts[1] == "page")
            {
                number = Math.Clamp(number, 0, Math.Max(0, (session.Candidates.Count - 1) / PageSize));
            }
            else
            {
                await _bot.AnswerCallbackQueryAsync(callback.Id, cancellationToken: ct);
                return;
            }

            await _bot.AnswerCallbackQueryAsync(callback.Id, cancellationToken: ct);
            await SendPreviewAsync(user, chatId, session, number, callback.Message!.MessageId, ct);
            return;
        }

        if (data is "import:all" or "import:none")
        {
            var selected = data == "import:all";
            foreach (var candidate in session.Candidates)
            {
                candidate.Selected = selected && candidate.Status == "New";
            }

            await _sessions.ReplaceOneAsync(
                x => x.ChatId == chatId && x.UserId == user.Id && x.Status == "Preview",
                session, cancellationToken: ct);

            await _bot.AnswerCallbackQueryAsync(callback.Id, cancellationToken: ct);
            await SendPreviewAsync(user, chatId, session, 0, callback.Message!.MessageId, ct);
            return;
        }

        await _bot.AnswerCallbackQueryAsync(callback.Id, cancellationToken: ct);
    }

    private async Task CommitAsync(
        User user, long chatId, int messageId, CancellationToken ct)
    {
        var locked = await _sessions.FindOneAndUpdateAsync<BirthdayImportSessionDocument>(
            x => x.ChatId == chatId && x.UserId == user.Id &&
                 x.Status == "Preview" && x.ExpiresAtUtc > DateTime.UtcNow,
            Builders<BirthdayImportSessionDocument>.Update.Set(x => x.Status, "Committing"),
            new FindOneAndUpdateOptions<BirthdayImportSessionDocument>
            {
                ReturnDocument = ReturnDocument.After
            }, ct);

        if (locked is null)
        {
            await _bot.SendTextMessageAsync(chatId,
                "This import is already processing or has expired.",
                cancellationToken: ct);
            return;
        }

        try
        {
            var selected = locked.Candidates.Where(x => x.Selected).ToArray();
            var existing = await _database.Birthdays
                .Find(x => x.UserId == user.Id).ToListAsync(ct);
            var seen = new HashSet<string>(
                existing.Select(b => Key(b.FullName, b.Date)), StringComparer.Ordinal);
            var newEntries = new List<Birthday>();
            var conflicts = 0;

            foreach (var candidate in selected)
            {
                var fingerprint = Key(candidate.FullName, candidate.Date);
                if (!seen.Add(fingerprint)) continue;

                if (existing.Any(b => Normalize(b.FullName) == Normalize(candidate.FullName) &&
                                      (b.Date.Month != candidate.Date.Month || b.Date.Day != candidate.Date.Day)))
                {
                    // Explicit conflict selection is honored, as the user saw the warning.
                    if (candidate.Status != "Conflict")
                    {
                        conflicts++;
                        continue;
                    }
                }

                newEntries.Add(new Birthday
                {
                    UserId = user.Id,
                    Name = candidate.FirstName,
                    LastName = candidate.LastName,
                    Date = candidate.Date,
                    BirthYearKnown = candidate.YearKnown,
                    TimeZoneId = user.Timezone
                });
            }

            if (newEntries.Count > 0)
            {
                await _database.Birthdays.InsertManyAsync(newEntries, cancellationToken: ct);
            }

            await _sessions.UpdateOneAsync(
                x => x.ChatId == chatId && x.UserId == user.Id,
                Builders<BirthdayImportSessionDocument>.Update.Set(x => x.Status, "Done"),
                cancellationToken: ct);

            var resultMessage = user.Lang switch
            {
                Language.Ru =>
                    $"✅ Импорт завершён. Добавлено: {newEntries.Count}. " +
                    $"Пропущено: {selected.Length - newEntries.Count}.",
                Language.Pl => $"✅ Import zakończony. Dodano: {newEntries.Count}.",
                _ => $"✅ Import complete. Added: {newEntries.Count}. Skipped: {selected.Length - newEntries.Count}."
            };

            await _bot.EditMessageTextAsync(
                chatId, messageId, resultMessage,
                replyMarkup: Keyboards.BackToMenuKb(user.Lang),
                cancellationToken: ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Contact import commit failed (user {UserId})", user.Id);
            await _sessions.UpdateOneAsync(
                x => x.ChatId == chatId && x.UserId == user.Id,
                Builders<BirthdayImportSessionDocument>.Update.Set(x => x.Status, "Preview"),
                cancellationToken: CancellationToken.None);

            await _bot.SendTextMessageAsync(chatId,
                "Import failed. You can try again. Existing birthdays will be checked again to avoid duplicates.",
                cancellationToken: ct);
        }
    }

    private async Task SendPreviewAsync(
        User user,
        long chatId,
        BirthdayImportSessionDocument session,
        int page,
        int? messageId,
        CancellationToken ct)
    {
        var total = session.Candidates.Count;
        var pages = Math.Max(1, (total + PageSize - 1) / PageSize);
        page = Math.Clamp(page, 0, pages - 1);
        var selected = session.Candidates.Count(x => x.Selected);
        var duplicates = session.Candidates.Count(x => x.Status == "Duplicate");
        var conflicts = session.Candidates.Count(x => x.Status == "Conflict");
        var title = user.Lang switch
        {
            Language.Ru =>
                $"📥 <b>Предпросмотр импорта</b>\n" +
                $"С датой рождения: {total}; без даты: {session.WithoutBirthday}; некорректных: {session.Invalid}.\n" +
                $"Дубликаты: {duplicates}, совпадения имён с другой датой: {conflicts}.\n" +
                $"Выбрано: <b>{selected}</b>. Страница {page + 1}/{pages}.\n\n" +
                "✅ — импортировать, ☐ — пропустить. Нажми на имя, чтобы изменить выбор.",
            Language.Pl =>
                $"📥 <b>Podgląd importu</b>\nDaty: {total}, duplikaty: {duplicates}, konflikty: {conflicts}. " +
                $"Wybrano: {selected}. Strona {page + 1}/{pages}.",
            _ =>
                $"📥 <b>Import preview</b>\nBirthdays: {total}, duplicates: {duplicates}, name conflicts: {conflicts}. " +
                $"Selected: {selected}. Page {page + 1}/{pages}."
        };

        var rows = new List<InlineKeyboardButton[]>();
        foreach (var row in session.Candidates.Skip(page * PageSize).Take(PageSize))
        {
            var sign = row.Status == "Duplicate" ? "⛔" : row.Selected ? "✅" : "☐";
            var suffix = row.Status == "Conflict" ? " ⚠️" : "";
            var label = $"{sign} {row.FullName} — {FormatDate(row.Date, row.YearKnown)}{suffix}";
            if (label.Length > 60) label = label[..60];
            rows.Add(new[]
            {
                InlineKeyboardButton.WithCallbackData(label, $"import:toggle:{row.Index}")
            });
        }

        var navigation = new List<InlineKeyboardButton>();
        if (page > 0)
            navigation.Add(InlineKeyboardButton.WithCallbackData("◀", $"import:page:{page - 1}"));
        if (page + 1 < pages)
            navigation.Add(InlineKeyboardButton.WithCallbackData("▶", $"import:page:{page + 1}"));
        if (navigation.Count > 0) rows.Add(navigation.ToArray());

        rows.Add(new[]
        {
            InlineKeyboardButton.WithCallbackData(
                user.Lang == Language.Ru ? "✅ Все новые" : "Select new", "import:all"),
            InlineKeyboardButton.WithCallbackData(
                user.Lang == Language.Ru ? "☐ Снять выбор" : "Select none", "import:none")
        });
        rows.Add(new[]
        {
            InlineKeyboardButton.WithCallbackData(
                user.Lang == Language.Ru ? $"📥 Импортировать ({selected})" : $"Import ({selected})", "import:save")
        });
        rows.Add(new[]
        {
            InlineKeyboardButton.WithCallbackData(
                user.Lang == Language.Ru ? "❌ Отмена" : "Cancel", "import:cancel")
        });

        var markup = new InlineKeyboardMarkup(rows);
        if (messageId.HasValue)
        {
            try
            {
                await _bot.EditMessageTextAsync(
                    chatId, messageId.Value, title,
                    parseMode: ParseMode.Html,
                    replyMarkup: markup, cancellationToken: ct);
            }
            catch (Telegram.Bot.Exceptions.ApiRequestException ex)
                when (ex.Message.Contains("message is not modified", StringComparison.OrdinalIgnoreCase))
            {
                // Telegram says nothing changed; this is expected on a repeated click.
            }
        }
        else
        {
            await _bot.SendTextMessageAsync(
                chatId, title,
                parseMode: ParseMode.Html,
                replyMarkup: markup, cancellationToken: ct);
        }
    }

    private static string FormatDate(DateOnly date, bool known) =>
        date.ToString(known ? "dd.MM.yyyy" : "dd.MM", CultureInfo.InvariantCulture);

    private static string Normalize(string name)
    {
        var source = name.Trim().Normalize(NormalizationForm.FormD);
        var result = new StringBuilder();
        var lastSpace = false;
        foreach (var ch in source)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark)
                continue;
            if (char.IsWhiteSpace(ch))
            {
                if (!lastSpace) result.Append(' ');
                lastSpace = true;
            }
            else
            {
                result.Append(char.ToUpperInvariant(ch));
                lastSpace = false;
            }
        }
        return result.ToString().Trim();
    }

    private static string Key(string name, DateOnly date) =>
        $"{Normalize(name)}|{date.Month:D2}{date.Day:D2}";
}

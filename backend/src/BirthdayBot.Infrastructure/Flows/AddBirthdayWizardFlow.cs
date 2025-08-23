using BirthdayBot.Application.Interfaces;
using BirthdayBot.Application.Models;
using BirthdayBot.Application.UI;
using BirthdayBot.Application.Utils;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace BirthdayBot.Application.Flows;

public sealed class AddBirthdayWizardFlow
{
    private readonly ITelegramBotClient _bot;
    private readonly IAddBirthdayWizardSessionStore _store;
    private readonly ITimeZoneResolver _tzResolver;
    private readonly IUserRepository _users;       // using existing user repository
    private readonly IBirthdayRepository _birthdays;          // using existing birthday repository
    private readonly ILogger<AddBirthdayWizardFlow> _log;

    public AddBirthdayWizardFlow(
        ITelegramBotClient bot,
        IAddBirthdayWizardSessionStore store,
        ITimeZoneResolver tzResolver,
        IUserRepository users,
        IBirthdayRepository birthdays,
        ILogger<AddBirthdayWizardFlow> log)
    {
        _bot = bot; _store = store; _tzResolver = tzResolver;
        _users = users; _birthdays = birthdays; _log = log;
    }

    public async Task<bool> TryHandleAsync(Update u, CancellationToken ct)
    {
        var msg = u.Message;
        var cb  = u.CallbackQuery;

        // Старт команды
        if (msg?.Text == "/add_birthday")
        {
            var s = _store.GetOrCreate(msg.Chat.Id, msg.From!.Id);
            s.Step = AddWizardStep.Name;
            s.Name = s.Relation = s.TimeZoneId = null; s.Date = null; s.WaitingCity = false;
            _store.Upsert(s);

            await _bot.SendTextMessageAsync(msg.Chat, 
                "<b>🎉 Добавляем ДР</b>\n① <b>Имя</b> → ② Дата → ③ Часовой пояс → ④ Отношение → ⑤ Подтверждение\n\n" +
                "Отправьте имя именинника (например: <code>Иван</code>).",
                parseMode: ParseMode.Html, cancellationToken: ct);
            return true;
        }

        // Callback из подтверждения
        if (cb?.Data is { } data && data.StartsWith("add:"))
        {
            var s = _store.Get(cb.Message!.Chat.Id, cb.From.Id);
            if (s is null) return false;

            if (data == "add:cancel")
            {
                _store.Remove(s.ChatId, s.UserId);
                await _bot.AnswerCallbackQueryAsync(cb.Id, "Отменено", cancellationToken: ct);
                await _bot.EditMessageTextAsync(cb.Message.Chat, cb.Message.MessageId, "Добавление отменено.");
                return true;
            }
            if (data == "add:edit")
            {
                s.Step = AddWizardStep.Name;
                _store.Upsert(s);
                await _bot.AnswerCallbackQueryAsync(cb.Id, cancellationToken: ct);
                await _bot.EditMessageTextAsync(cb.Message.Chat, cb.Message.MessageId,
                    "Изменим. Отправьте имя:", parseMode: ParseMode.Html);
                return true;
            }
            if (data == "add:ok")
            {
                try
                {
                    await _birthdays.CreateAsync(new()
                    {
                        Name = s.Name!,
                        Date = s.Date!.Value,
                        Relation = s.Relation,
                        TimeZoneId = s.TimeZoneId
                    }, ct);

                    _store.Remove(s.ChatId, s.UserId);
                    await _bot.AnswerCallbackQueryAsync(cb.Id, "Сохранено ✅", cancellationToken: ct);
                    await _bot.EditMessageTextAsync(cb.Message.Chat, cb.Message.MessageId, "Сохранено ✅");
                }
                catch (Exception ex)
                {
                    _log.LogError(ex, "Save birthday failed");
                    await _bot.AnswerCallbackQueryAsync(cb.Id, "Ошибка сохранения", cancellationToken: ct);
                }
                return true;
            }
        }

        // Шаги мастера
        if (msg is not null)
        {
            var s = _store.Get(msg.Chat.Id, msg.From!.Id);
            if (s is null) return false;

            // Отмена
            if (msg.Text == "❌ Отмена")
            {
                _store.Remove(s.ChatId, s.UserId);
                await _bot.SendTextMessageAsync(msg.Chat, "Отменено.", cancellationToken: ct);
                return true;
            }

            // ① Имя
            if (s.Step == AddWizardStep.Name)
            {
                if (string.IsNullOrWhiteSpace(msg.Text))
                {
                    await _bot.SendTextMessageAsync(msg.Chat, "Имя не распознано. Введите, например: <code>Иван</code>.",
                        parseMode: ParseMode.Html, cancellationToken: ct);
                    return true;
                }

                s.Name = msg.Text.Trim();
                s.Step = AddWizardStep.Date; _store.Upsert(s);

                await _bot.SendTextMessageAsync(msg.Chat,
                    "<b>🗓 Дата</b>\nФормат: <code>ДД.ММ</code> или <code>ГГГГ-ММ-ДД</code>.\nМожно нажать «Сегодня» / «Завтра».",
                    parseMode: ParseMode.Html, replyMarkup: Keyboards.DateKb, cancellationToken: ct);
                return true;
            }

            // ② Дата
            if (s.Step == AddWizardStep.Date)
            {
                DateOnly? date = null;
                if (msg.Text == "Сегодня") date = DateOnly.FromDateTime(DateTime.UtcNow);
                else if (msg.Text == "Завтра") date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1));
                else if (!string.IsNullOrWhiteSpace(msg.Text))
                {
                    var t = msg.Text.Trim();
                    if (DateOnly.TryParseExact(t, "yyyy-MM-dd", out var dYmd)) date = dYmd;
                    else if (DateOnly.TryParseExact($"{DateTime.UtcNow.Year}-{t}", "yyyy-dd.MM", out var dDm)) date = dDm; // «ДД.ММ»
                    else if (DateOnly.TryParse(t, out var any)) date = any;
                }

                if (date is null)
                {
                    await _bot.SendTextMessageAsync(msg.Chat,
                        "Не понял дату. Формат: <code>ДД.ММ</code> или <code>ГГГГ-ММ-ДД</code>. Либо «Сегодня/Завтра».",
                        parseMode: ParseMode.Html, replyMarkup: Keyboards.DateKb, cancellationToken: ct);
                    return true;
                }

                s.Date = date.Value;
                s.Step = AddWizardStep.TimeZone; _store.Upsert(s);

                await _bot.SendTextMessageAsync(msg.Chat,
                    "<b>🌍 Часовой пояс</b>\nПришлите геопозицию, введите город или IANA (например: <code>Europe/Warsaw</code>),\n" +
                    "либо нажмите «➡️ Пропустить» — возьмём из ваших настроек.",
                    parseMode: ParseMode.Html, replyMarkup: Keyboards.TimeZoneKb, cancellationToken: ct);
                return true;
            }

            // ③ Часовой пояс
            if (s.Step == AddWizardStep.TimeZone)
            {
                // Геопозиция
                if (msg.Location is { } loc)
                {
                    var tz = await _tzResolver.FromLocationAsync(loc.Latitude, loc.Longitude, ct);
                    if (tz is null)
                    {
                        await _bot.SendTextMessageAsync(msg.Chat,
                            "Не вышло определить пояс. Введите город (например: <code>Warsaw</code>) или нажмите «➡️ Пропустить».",
                            parseMode: ParseMode.Html, replyMarkup: Keyboards.TimeZoneKb, cancellationToken: ct);
                        return true;
                    }
                    s.TimeZoneId = tz; goto AskRelation;
                }

                // Пользователь нажал «🔎 Ввести город»
                if (msg.Text == "🔎 Ввести город")
                {
                    s.WaitingCity = true; _store.Upsert(s);
                    await _bot.SendTextMessageAsync(msg.Chat, "Введите город, например: <code>Warsaw</code>.",
                        parseMode: ParseMode.Html, cancellationToken: ct);
                    return true;
                }

                // Прислали город
                if (s.WaitingCity && !string.IsNullOrWhiteSpace(msg.Text))
                {
                    var tz = await _tzResolver.FromCityAsync(msg.Text.Trim(), ct);
                    s.WaitingCity = false;
                    if (tz is null)
                    {
                        await _bot.SendTextMessageAsync(msg.Chat,
                            "Город не найден. Попробуйте ещё или нажмите «➡️ Пропустить».",
                            replyMarkup: Keyboards.TimeZoneKb, cancellationToken: ct);
                        return true;
                    }
                    s.TimeZoneId = tz; goto AskRelation;
                }

                // Пропустить
                if (msg.Text == "➡️ Пропустить")
                {
                    var user = await _users.GetByTelegramUserIdAsync(s.UserId, ct);
                    s.TimeZoneId = user?.Timezone ?? "Europe/Warsaw";
                    goto AskRelation;
                }

                // Ввели IANA?
                if (!string.IsNullOrWhiteSpace(msg.Text) && _tzResolver.IsValidTz(msg.Text))
                {
                    s.TimeZoneId = msg.Text.Trim(); goto AskRelation;
                }

                await _bot.SendTextMessageAsync(msg.Chat,
                    "Не понял. Пришлите геопозицию, город или IANA (например, Europe/Warsaw), либо «➡️ Пропустить».",
                    replyMarkup: Keyboards.TimeZoneKb, cancellationToken: ct);
                return true;

            AskRelation:
                s.Step = AddWizardStep.Relation; _store.Upsert(s);
                await _bot.SendTextMessageAsync(msg.Chat,
                    "<b>👥 Отношение</b>\nКто это для вас? Выберите кнопку или введите свой вариант.",
                    parseMode: ParseMode.Html, replyMarkup: Keyboards.RelationKb, cancellationToken: ct);
                return true;
            }

            // ④ Отношение
            if (s.Step == AddWizardStep.Relation && msg.Text is { } rel)
            {
                s.Relation = rel is "❌ Отмена" ? null : rel;
                s.Step = AddWizardStep.Confirm; _store.Upsert(s);

                var text =
                    $"<b>✅ Проверим</b>\n" +
                    $"Имя: <b>{Formatting.Html(s.Name!)}</b>\n" +
                    $"Дата: <code>{s.Date:yyyy-MM-dd}</code>\n" +
                    $"Пояс: <code>{s.TimeZoneId}</code>\n" +
                    $"Отношение: <b>{Formatting.Html(s.Relation ?? "—")}</b>\nСохранить?";

                await _bot.SendTextMessageAsync(msg.Chat, text, parseMode: ParseMode.Html,
                    replyMarkup: Keyboards.ConfirmKb, cancellationToken: ct);
                return true;
            }
        }

        return false;
    }
}
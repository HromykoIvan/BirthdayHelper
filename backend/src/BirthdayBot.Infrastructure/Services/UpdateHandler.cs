using System.Globalization;
using System.Net;
using System.Text;
using System.Diagnostics;
using BirthdayBot.Application.Interfaces;
using BirthdayBot.Application.UI;
using BirthdayBot.Application.Services;
using BirthdayBot.Application.Models;
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
using Microsoft.Extensions.Options;
using BirthdayBot.Infrastructure.Options;

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
    private const int PageSize = 10;

    private readonly ILogger<UpdateHandler> _logger;
    private readonly ITelegramBotClient _bot;
    private readonly IUserRepository _users;
    private readonly IBirthdayRepository _birthdays;
    private readonly ILocalizationService _i18n;
    private readonly IUpcomingService _upcoming;
    private readonly AddBirthdayWizardFlow _wizard;
    private readonly IDateTimeZoneProvider _tzdb;
    private readonly IIntentRouter _intentRouter;
    private readonly IAiGreetingEnhancer _enhancer;
    private readonly IGreetingGenerator _greetings;
    private readonly IUserUpdateRateLimiter _userRateLimiter;
    private readonly AiMetrics _metrics;
    private readonly IAiEventRepository _aiEvents;
    private readonly PromptProfileOptions _promptProfiles;

    public UpdateHandler(
        ILogger<UpdateHandler> logger,
        ITelegramBotClient bot,
        IUserRepository users,
        IBirthdayRepository birthdays,
        ILocalizationService i18n,
        IUpcomingService upcoming,
        AddBirthdayWizardFlow wizard,
        IIntentRouter intentRouter,
        IAiGreetingEnhancer enhancer,
        IGreetingGenerator greetings,
        IUserUpdateRateLimiter userRateLimiter,
        AiMetrics metrics,
        IAiEventRepository aiEvents,
        IOptions<PromptProfileOptions> promptProfiles,
        IDateTimeZoneProvider? tzdb = null)
    {
        _logger = logger;
        _bot = bot;
        _users = users;
        _birthdays = birthdays;
        _i18n = i18n;
        _upcoming = upcoming;
        _wizard = wizard;
        _intentRouter = intentRouter;
        _enhancer = enhancer;
        _greetings = greetings;
        _userRateLimiter = userRateLimiter;
        _metrics = metrics;
        _aiEvents = aiEvents;
        _promptProfiles = promptProfiles.Value;
        _tzdb = tzdb ?? DateTimeZoneProviders.Tzdb;
        
        // Initialize Keyboards with localization service
        Keyboards.Initialize(i18n);
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
                if (data.StartsWith("lang:", StringComparison.Ordinal))
                {
                    await HandleLanguageSelectionAsync(update, data, ct);
                    return;
                }

                if (data.StartsWith("settings:", StringComparison.Ordinal))
                {
                    await HandleSettingsCallbackAsync(update, data, ct);
                    return;
                }

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
                // Try to get user language for error message
                var errorUser = update.CallbackQuery?.From != null
                    ? await _users.GetByTelegramUserIdAsync(update.CallbackQuery.From.Id, ct)
                    : null;
                var lang = errorUser?.Lang ?? Language.En;
                await SafeAnswerCallbackQuery(cqid, _i18n.GetText(lang, "error_try_again"), ct);
            }
        }
    }

    // ════════════════════════════════════════════
    //  Text messages
    // ════════════════════════════════════════════

    private async Task HandleTextMessageAsync(Message msg, CancellationToken ct)
    {
        if (msg is null) return;
        if (!_userRateLimiter.IsAllowed(msg.From?.Id ?? 0))
        {
            await _bot.SendTextMessageAsync(msg.Chat.Id, _i18n.GetText(Language.En, "rate_limit_exceeded"), cancellationToken: ct);
            return;
        }

        var chatId = msg.Chat?.Id ?? msg.From?.Id ?? 0;
        if (chatId == 0) return;

        var text = msg.Text!.Trim();
        
        // Check if user exists - if not, show language selection
        var existingUser = await _users.GetByTelegramUserIdAsync(msg.From!.Id, ct);
        if (existingUser == null && text.StartsWith("/start", StringComparison.OrdinalIgnoreCase))
        {
            await SendLanguageSelection(chatId, ct);
            return;
        }

        var user = await EnsureUser(msg.From!, ct);

        if (text.StartsWith("/start", StringComparison.OrdinalIgnoreCase) ||
            text.StartsWith("/help", StringComparison.OrdinalIgnoreCase))
        {
            await SendMainMenu(chatId, msg.From!, user, ct);
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
                await SendDeletePageAsync(user, chatId, page: 0, messageId: null, ct);
                return;
            }

            var b = await _birthdays.FindByNameAsync(user.Id, name, ct);
            if (b == null)
            {
                await _bot.SendTextMessageAsync(chatId, _i18n.GetText(user.Lang, "entry_not_found"),
                    replyMarkup: Keyboards.BackToMenuKb(user.Lang), cancellationToken: ct);
                return;
            }

            await _birthdays.DeleteAsync(b.Id, user.Id, ct);
            await _bot.SendTextMessageAsync(chatId,
                _i18n.GetText(user.Lang, "removed"),
                replyMarkup: Keyboards.BackToMenuKb(user.Lang), cancellationToken: ct);
            return;
        }

        if (text.StartsWith("/settings", StringComparison.OrdinalIgnoreCase))
        {
            await _bot.SendTextMessageAsync(chatId,
                BuildSettingsMessage(user),
                parseMode: ParseMode.Html,
                replyMarkup: Keyboards.SettingsKb(user.Lang, user),
                cancellationToken: ct);
            return;
        }

        // AI/local intent router for free text commands.
        if (await TryHandleIntentAsync(user, msg.From!, chatId, text, ct))
            return;

        // Fallback — show main menu
        var fallbackUser = await EnsureUser(msg.From!, ct);
        await SendMainMenu(chatId, msg.From!, fallbackUser, ct);
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
                var homeUser = await EnsureUser(cq.From, ct);
                var homeName = Formatting.Html(cq.From.FirstName ?? "");
                var homeWelcomeText = string.Format(_i18n.GetText(homeUser.Lang, "welcome"), homeName);
                await SafeEditMessageAsync(chatId, cq.Message.MessageId,
                    homeWelcomeText,
                    ParseMode.Html, Keyboards.MainMenuKb(homeUser.Lang), ct);
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
                await ShowSettingsMenu(chatId, cq.Message.MessageId, user, ct);
                break;

            case "menu:help":
                await SafeEditMessageAsync(chatId, cq.Message.MessageId,
                    _i18n.GetText(user.Lang, "help"),
                    ParseMode.Html, Keyboards.BackToMenuKb(user.Lang), ct);
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
                await SendAllBirthdaysPageAsync(user, chatId, page: 0, messageId: cq.Message.MessageId, ct);
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
                ? _i18n.GetText(user.Lang, "upcoming_empty_period")
                : BuildUpcomingHtml(user.Lang, items);

            await SafeEditMessageAsync(chatId, cq.Message.MessageId,
                text, ParseMode.Html, Keyboards.UpcomingKb(user.Lang), ct);

            await SafeAnswerCallbackQuery(cq.Id, ct: ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to handle upcoming filter callback: {Data}", data);
            await SafeAnswerCallbackQuery(cq.Id, _i18n.GetText(user.Lang, "error_try_again"), ct);
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
                await SendAllBirthdaysPageAsync(user, chatId, page: 0, messageId: cq.Message.MessageId, ct);
                return;
            }

            if (data.StartsWith("list:all:", StringComparison.Ordinal))
            {
                var pageText = data["list:all:".Length..];
                var page = int.TryParse(pageText, out var parsedPage) ? parsedPage : 0;
                await SafeAnswerCallbackQuery(cq.Id, ct: ct);
                await SendAllBirthdaysPageAsync(user, chatId, page, cq.Message.MessageId, ct);
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
            await SafeAnswerCallbackQuery(cq.Id, _i18n.GetText(user.Lang, "error_try_again"), ct);
        }
    }

    // ════════════════════════════════════════════
    //  Other callback queries (delete:*)
    // ════════════════════════════════════════════

    private async Task HandleCallbackQueryAsync(CallbackQuery cq, CancellationToken ct)
    {
        if (!_userRateLimiter.IsAllowed(cq.From.Id))
        {
            await SafeAnswerCallbackQuery(cq.Id, _i18n.GetText(Language.En, "rate_limit_exceeded"), ct);
            return;
        }

        var user = await EnsureUser(cq.From, ct);

        try
        {
            if (cq.Data is { } data && data.StartsWith("delete:list:", StringComparison.Ordinal))
            {
                var pageText = data["delete:list:".Length..];
                var page = int.TryParse(pageText, out var parsedPage) ? parsedPage : 0;
                await SendDeletePageAsync(user, cq.Message!.Chat.Id, page, cq.Message.MessageId, ct);
            }
            else if (cq.Data is { } pickData && pickData.StartsWith("delete:pick:", StringComparison.Ordinal))
            {
                var parts = pickData.Split(':');
                if (parts.Length == 4 &&
                    ObjectId.TryParse(parts[2], out var pickedId) &&
                    int.TryParse(parts[3], out var page))
                {
                    var entry = await _birthdays.GetByIdAsync(pickedId, user.Id, ct);
                    if (entry is null)
                    {
                        await SafeAnswerCallbackQuery(cq.Id, _i18n.GetText(user.Lang, "entry_not_found"), ct);
                        return;
                    }

                    var text = string.Format(
                        _i18n.GetText(user.Lang, "delete_confirm"),
                        Formatting.Html(entry.FullName),
                        $"{entry.Date:dd.MM.yyyy}");

                    await SafeEditMessageAsync(
                        cq.Message!.Chat.Id,
                        cq.Message.MessageId,
                        text,
                        ParseMode.Html,
                        BuildDeleteConfirmKeyboard(user.Lang, entry.Id, page),
                        ct);
                }
            }
            else if (cq.Data is { } confirmData && confirmData.StartsWith("delete:confirm:", StringComparison.Ordinal))
            {
                var parts = confirmData.Split(':');
                if (parts.Length == 4 &&
                    ObjectId.TryParse(parts[2], out var deleteId) &&
                    int.TryParse(parts[3], out var page))
                {
                    await _birthdays.DeleteAsync(deleteId, user.Id, ct);
                    await SafeAnswerCallbackQuery(cq.Id, _i18n.GetText(user.Lang, "removed"), ct);
                    await SendDeletePageAsync(user, cq.Message!.Chat.Id, page, cq.Message.MessageId, ct);
                    return;
                }
            }
            else if (cq.Data is { } cancelData && cancelData.StartsWith("delete:cancel:", StringComparison.Ordinal))
            {
                var pageText = cancelData["delete:cancel:".Length..];
                var page = int.TryParse(pageText, out var parsedPage) ? parsedPage : 0;
                await SendDeletePageAsync(user, cq.Message!.Chat.Id, page, cq.Message.MessageId, ct);
            }
            else if (cq.Data is { } legacyDeleteData && legacyDeleteData.StartsWith("delete:", StringComparison.Ordinal))
            {
                var idStr = legacyDeleteData["delete:".Length..];
                if (ObjectId.TryParse(idStr, out var bid))
                {
                    await _birthdays.DeleteAsync(bid, user.Id, ct);

                    if (cq.Message is not null)
                    {
                        await SafeEditMessageAsync(
                            cq.Message.Chat.Id,
                            cq.Message.MessageId,
                            _i18n.GetText(user.Lang, "removed"),
                            null, Keyboards.BackToMenuKb(user.Lang), ct);
                    }
                }
            }
            else if (cq.Data is { } aiData && aiData.StartsWith("ai:improve:", StringComparison.Ordinal))
            {
                var idText = aiData["ai:improve:".Length..];
                if (ObjectId.TryParse(idText, out var bid))
                {
                    var entry = await _birthdays.GetByIdAsync(bid, user.Id, ct);
                    if (entry is null)
                    {
                        await SafeAnswerCallbackQuery(cq.Id, _i18n.GetText(user.Lang, "entry_not_found"), ct);
                        return;
                    }

                    var zone = _tzdb[user.Timezone];
                    var today = SystemClock.Instance.GetCurrentInstant().InZone(zone).Date;
                    var (_, age) = DateHelpers.NextBirthday(today, entry.Date);
                    var draft = _greetings.GeneratePersonalized(user, entry, age);
                    var improveSw = Stopwatch.StartNew();
                    var improved = await _enhancer.EnhanceAsync(user, entry, draft, age, ct);
                    improveSw.Stop();

                    await _aiEvents.CreateAsync(new AiEvent
                    {
                        UserId = user.Id,
                        TelegramUserId = user.TelegramUserId,
                        EventType = "enhance",
                        InputText = draft,
                        OutputText = improved.Text,
                        IsFallback = improved.IsFallback,
                        FallbackReason = improved.FallbackReason,
                        PromptVersion = improved.PromptVersion,
                        ModelSource = improved.ModelSource,
                        LatencyMs = improveSw.Elapsed.TotalMilliseconds
                    }, ct);

                    var text = $"✨ <b>{_i18n.GetText(user.Lang, "ai_improved_title")}</b>\n\n{Formatting.Html(improved.Text)}";
                    await SafeEditMessageAsync(
                        cq.Message!.Chat.Id,
                        cq.Message.MessageId,
                        text,
                        ParseMode.Html,
                        Keyboards.BackToMenuKb(user.Lang),
                        ct);
                }
            }
            else if (cq.Data is { } removeByNameData && removeByNameData.StartsWith("delete:name:confirm:", StringComparison.Ordinal))
            {
                var encodedName = removeByNameData["delete:name:confirm:".Length..];
                var name = WebUtility.UrlDecode(encodedName);
                if (string.IsNullOrWhiteSpace(name))
                {
                    await SafeAnswerCallbackQuery(cq.Id, _i18n.GetText(user.Lang, "error_try_again"), ct);
                    return;
                }
                var entry = await _birthdays.FindByNameAsync(user.Id, name, ct);
                if (entry is null)
                {
                    await SafeAnswerCallbackQuery(cq.Id, _i18n.GetText(user.Lang, "entry_not_found"), ct);
                    return;
                }

                await _birthdays.DeleteAsync(entry.Id, user.Id, ct);
                await SafeEditMessageAsync(
                    cq.Message!.Chat.Id,
                    cq.Message.MessageId,
                    _i18n.GetText(user.Lang, "removed"),
                    ParseMode.Html,
                    Keyboards.BackToMenuKb(user.Lang),
                    ct);
            }
            else if (cq.Data is { } removeByNameCancel && removeByNameCancel.StartsWith("delete:name:cancel", StringComparison.Ordinal))
            {
                await SafeEditMessageAsync(
                    cq.Message!.Chat.Id,
                    cq.Message.MessageId,
                    _i18n.GetText(user.Lang, "delete_cancelled"),
                    ParseMode.Html,
                    Keyboards.BackToMenuKb(user.Lang),
                    ct);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to handle callback '{Data}'", cq.Data);
            await SafeAnswerCallbackQuery(cq.Id, _i18n.GetText(user.Lang, "error_try_again"), ct);
            return;
        }

        await SafeAnswerCallbackQuery(cq.Id, ct: ct);
    }

    // ════════════════════════════════════════════
    //  Settings callbacks (settings:*)
    // ════════════════════════════════════════════

    private async Task HandleSettingsCallbackAsync(Update update, string data, CancellationToken ct)
    {
        var cq = update.CallbackQuery!;
        var chatId = cq.Message!.Chat.Id;
        var user = await EnsureUser(cq.From, ct);

        try
        {
            if (data == "settings:time")
            {
                await SafeEditMessageAsync(chatId, cq.Message.MessageId,
                    _i18n.GetText(user.Lang, "settings_select_time"),
                    ParseMode.Html, Keyboards.TimePickerKb(user.Lang), ct);
                await SafeAnswerCallbackQuery(cq.Id, ct: ct);
                return;
            }

            if (data.StartsWith("settings:time:", StringComparison.Ordinal))
            {
                var timeStr = data["settings:time:".Length..];
                if (DateHelpers.TryParseTimeHHmm(timeStr, out var h, out var m))
                {
                    user.NotifyAtLocalTime = $"{h:00}:{m:00}";
                    await _users.UpdateAsync(user, ct);
                    await ShowSettingsMenu(chatId, cq.Message.MessageId, user, ct);
                    await SafeAnswerCallbackQuery(cq.Id, _i18n.GetText(user.Lang, "saved"), ct);
                    return;
                }
            }

            if (data == "settings:lang")
            {
                await SafeEditMessageAsync(chatId, cq.Message.MessageId,
                    _i18n.GetText(user.Lang, "select_language"),
                    ParseMode.Html, Keyboards.LanguageSelectionKb("settings:lang:"), ct);
                await SafeAnswerCallbackQuery(cq.Id, ct: ct);
                return;
            }

            if (data.StartsWith("settings:lang:", StringComparison.Ordinal))
            {
                var langCode = data["settings:lang:".Length..];
                var selectedLang = langCode switch
                {
                    "ru" => Language.Ru,
                    "pl" => Language.Pl,
                    "en" => Language.En,
                    _ => user.Lang
                };
                user.Lang = selectedLang;
                await _users.UpdateAsync(user, ct);
                await ShowSettingsMenu(chatId, cq.Message.MessageId, user, ct);
                await SafeAnswerCallbackQuery(cq.Id, _i18n.GetText(user.Lang, "saved"), ct);
                return;
            }

            if (data == "settings:tz")
            {
                await SafeEditMessageAsync(chatId, cq.Message.MessageId,
                    _i18n.GetText(user.Lang, "settings_select_timezone"),
                    ParseMode.Html, Keyboards.CommonTimezonesKb(user.Lang), ct);
                await SafeAnswerCallbackQuery(cq.Id, ct: ct);
                return;
            }

            if (data.StartsWith("settings:tz:", StringComparison.Ordinal))
            {
                var tz = data["settings:tz:".Length..];
                if (tz == "input")
                {
                    // User wants to enter timezone manually - we'll handle it in text message handler
                    await SafeEditMessageAsync(chatId, cq.Message.MessageId,
                        _i18n.GetText(user.Lang, "settings_select_timezone"),
                        ParseMode.Html, Keyboards.BackToMenuKb(user.Lang), ct);
                    await SafeAnswerCallbackQuery(cq.Id, ct: ct);
                    return;
                }

                if (_tzdb.Ids.Contains(tz))
                {
                    user.Timezone = tz;
                    await _users.UpdateAsync(user, ct);
                    await ShowSettingsMenu(chatId, cq.Message.MessageId, user, ct);
                    await SafeAnswerCallbackQuery(cq.Id, _i18n.GetText(user.Lang, "saved"), ct);
                    return;
                }
            }

            if (data == "settings:auto")
            {
                user.AutoGenerateGreetings = !user.AutoGenerateGreetings;
                await _users.UpdateAsync(user, ct);
                await ShowSettingsMenu(chatId, cq.Message.MessageId, user, ct);
                await SafeAnswerCallbackQuery(cq.Id, _i18n.GetText(user.Lang, "saved"), ct);
                return;
            }

            if (data == "settings:tone")
            {
                user.Tone = user.Tone == Tone.Formal ? Tone.Friendly : Tone.Formal;
                await _users.UpdateAsync(user, ct);
                await ShowSettingsMenu(chatId, cq.Message.MessageId, user, ct);
                await SafeAnswerCallbackQuery(cq.Id, _i18n.GetText(user.Lang, "saved"), ct);
                return;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to handle settings callback '{Data}'", data);
            await SafeAnswerCallbackQuery(cq.Id, _i18n.GetText(user.Lang, "error_try_again"), ct);
        }
    }

    private string BuildSettingsMessage(BirthdayBot.Domain.Entities.User user)
    {
        var sb = new StringBuilder();
        sb.AppendLine(_i18n.GetText(user.Lang, "settings_title"));
        sb.AppendLine();

        // Notification time
        sb.AppendLine(string.Format(_i18n.GetText(user.Lang, "settings_notification_time"), user.NotifyAtLocalTime));

        // Interface language
        var langName = user.Lang switch
        {
            Language.Ru => _i18n.GetText(user.Lang, "lang_russian"),
            Language.Pl => _i18n.GetText(user.Lang, "lang_polish"),
            Language.En => _i18n.GetText(user.Lang, "lang_english"),
            _ => user.Lang.ToString()
        };
        sb.AppendLine(string.Format(_i18n.GetText(user.Lang, "settings_language"), langName));

        // Timezone
        sb.AppendLine(string.Format(_i18n.GetText(user.Lang, "settings_timezone"), user.Timezone));

        // Auto-greetings
        var autoStatus = user.AutoGenerateGreetings
            ? _i18n.GetText(user.Lang, "settings_on")
            : _i18n.GetText(user.Lang, "settings_off");
        sb.AppendLine(string.Format(_i18n.GetText(user.Lang, "settings_auto_greetings"), autoStatus));

        // Tone
        var toneName = user.Tone == Tone.Formal
            ? _i18n.GetText(user.Lang, "settings_formal")
            : _i18n.GetText(user.Lang, "settings_friendly");
        sb.AppendLine(string.Format(_i18n.GetText(user.Lang, "settings_tone"), toneName));

        return sb.ToString();
    }

    private async Task ShowSettingsMenu(long chatId, int messageId, BirthdayBot.Domain.Entities.User user, CancellationToken ct)
    {
        var message = BuildSettingsMessage(user);
        await SafeEditMessageAsync(chatId, messageId, message,
            ParseMode.Html, Keyboards.SettingsKb(user.Lang, user), ct);
    }

    // ════════════════════════════════════════════
    //  View builders
    // ════════════════════════════════════════════

    /// <summary>Sends the main menu with a welcome message.</summary>
    private async Task SendMainMenu(long chatId, Telegram.Bot.Types.User tgUser, BirthdayBot.Domain.Entities.User user, CancellationToken ct)
    {
        var name = Formatting.Html(tgUser.FirstName ?? "");
        var welcomeText = string.Format(_i18n.GetText(user.Lang, "welcome"), name);
        
        await _bot.SendTextMessageAsync(chatId,
            welcomeText,
            parseMode: ParseMode.Html,
            replyMarkup: Keyboards.MainMenuKb(user.Lang),
            cancellationToken: ct);
    }

    /// <summary>Sends language selection for new users.</summary>
    private async Task SendLanguageSelection(long chatId, CancellationToken ct)
    {
        // Use English as default for language selection screen
        await _bot.SendTextMessageAsync(chatId,
            _i18n.GetText(Language.En, "select_language"),
            parseMode: ParseMode.Html,
            replyMarkup: Keyboards.LanguageSelectionKb("lang:"),
            cancellationToken: ct);
    }

    /// <summary>Handles language selection callback (lang:ru, lang:pl, lang:en).</summary>
    private async Task HandleLanguageSelectionAsync(Update update, string data, CancellationToken ct)
    {
        var cq = update.CallbackQuery!;
        var chatId = cq.Message!.Chat.Id;
        var langCode = data["lang:".Length..];

        var selectedLang = langCode switch
        {
            "ru" => Language.Ru,
            "pl" => Language.Pl,
            "en" => Language.En,
            _ => Language.En
        };

        // Get or create user
        var user = await EnsureUser(cq.From, ct);
        user.Lang = selectedLang;
        await _users.UpdateAsync(user, ct);

        var langName = selectedLang switch
        {
            Language.Ru => _i18n.GetText(selectedLang, "lang_russian"),
            Language.Pl => _i18n.GetText(selectedLang, "lang_polish"),
            Language.En => _i18n.GetText(selectedLang, "lang_english"),
            _ => selectedLang.ToString()
        };

        await SafeAnswerCallbackQuery(cq.Id, string.Format(_i18n.GetText(selectedLang, "language_selected"), langName), ct);
        
        // Show main menu in selected language
        await SendMainMenu(chatId, cq.From, user, ct);
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
        sb.AppendLine($"📅 <b>{_i18n.GetText(user.Lang, $"month_name_{month}")} {year}</b>\n");

        if (items.Length == 0)
        {
            sb.AppendLine(_i18n.GetText(user.Lang, "no_birthdays_this_month"));
        }
        else
        {
            foreach (var i in items)
            {
                var dayStr = $"{i.NextDate.Day:D2}.{i.NextDate.Month:D2}";
                var relation = string.IsNullOrWhiteSpace(i.Relation) ? "" : $" · {Formatting.Html(i.Relation)}";
                sb.AppendLine($"🎂 <b>{Formatting.Html(i.Name)}</b> — {dayStr}, " +
                              $"{i.Age} {YearWord(user.Lang, i.Age)}{relation}");
            }
        }

        sb.AppendLine($"\n<i>{string.Format(_i18n.GetText(user.Lang, "total_entries"), list.Count)}</i>");

        var kb = BuildMonthNavigator(user.Lang, year, month);
        return (sb.ToString(), kb);
    }

    /// <summary>Sends a paginated list of all birthdays sorted by nearest upcoming date.</summary>
    private async Task SendAllBirthdaysPageAsync(
        BirthdayBot.Domain.Entities.User user,
        long chatId,
        int page,
        int? messageId,
        CancellationToken ct)
    {
        var list = await _birthdays.ListByUserAsync(user.Id, ct);
        if (list.Count == 0)
        {
            await SendOrEditAsync(chatId, messageId,
                _i18n.GetText(user.Lang, "all_entries_empty"),
                ParseMode.Html,
                Keyboards.MainMenuKb(user.Lang),
                ct);
            return;
        }

        var zone = _tzdb[user.Timezone];
        var today = SystemClock.Instance.GetCurrentInstant().InZone(zone).Date;
        var rows = BuildBirthdayRows(list, today);
        page = ClampPage(page, rows.Count);
        var pageRows = rows.Skip(page * PageSize).Take(PageSize).ToArray();
        var first = page * PageSize + 1;
        var last = first + pageRows.Length - 1;

        var sb = new StringBuilder();
        sb.AppendLine(_i18n.GetText(user.Lang, "all_entries"));
        sb.AppendLine();
        sb.AppendLine($"<i>{string.Format(_i18n.GetText(user.Lang, "showing_entries"), first, last, rows.Count)}</i>\n");

        foreach (var item in pageRows)
        {
            var relation = string.IsNullOrWhiteSpace(item.Birthday.Relation) ? "" : $" · {Formatting.Html(item.Birthday.Relation)}";
            var interests = string.IsNullOrWhiteSpace(item.Birthday.Interests) ? "" : $"\n   💡 {Formatting.Html(item.Birthday.Interests)}";
            var next = string.Format(_i18n.GetText(user.Lang, "next_occurrence"),
                $"{item.NextDate.Day:D2}.{item.NextDate.Month:D2}",
                item.Age,
                YearWord(user.Lang, item.Age));

            sb.AppendLine($"🎂 <b>{Formatting.Html(item.Birthday.FullName)}</b>");
            sb.AppendLine($"   📅 {item.Birthday.Date:dd.MM.yyyy} → {next}{relation}{interests}");
        }

        await SendOrEditAsync(chatId, messageId, sb.ToString(), ParseMode.Html,
            BuildAllEntriesKeyboard(user.Lang, page, rows.Count), ct);
    }

    /// <summary>Sends a paginated delete picker with compact numbered buttons.</summary>
    private async Task SendDeletePageAsync(
        BirthdayBot.Domain.Entities.User user,
        long chatId,
        int page,
        int? messageId,
        CancellationToken ct)
    {
        var list = await _birthdays.ListByUserAsync(user.Id, ct);
        if (list.Count == 0)
        {
            await SendOrEditAsync(chatId, messageId,
                _i18n.GetText(user.Lang, "all_entries_empty"),
                ParseMode.Html,
                Keyboards.MainMenuKb(user.Lang),
                ct);
            return;
        }

        var zone = _tzdb[user.Timezone];
        var today = SystemClock.Instance.GetCurrentInstant().InZone(zone).Date;
        var rows = BuildBirthdayRows(list, today);
        page = ClampPage(page, rows.Count);
        var pageRows = rows.Skip(page * PageSize).Take(PageSize).ToArray();
        var first = page * PageSize + 1;
        var last = first + pageRows.Length - 1;

        var sb = new StringBuilder();
        sb.AppendLine(_i18n.GetText(user.Lang, "delete_list_title"));
        sb.AppendLine();
        sb.AppendLine(_i18n.GetText(user.Lang, "delete_select_instruction"));
        sb.AppendLine($"<i>{string.Format(_i18n.GetText(user.Lang, "showing_entries"), first, last, rows.Count)}</i>\n");

        for (var i = 0; i < pageRows.Length; i++)
        {
            var item = pageRows[i];
            var number = first + i;
            var relation = string.IsNullOrWhiteSpace(item.Birthday.Relation) ? "" : $" · {Formatting.Html(item.Birthday.Relation)}";
            sb.AppendLine($"{number}. 🎂 <b>{Formatting.Html(item.Birthday.FullName)}</b> — {item.Birthday.Date:dd.MM.yyyy}{relation}");
        }

        await SendOrEditAsync(chatId, messageId, sb.ToString(), ParseMode.Html,
            BuildDeleteListKeyboard(user.Lang, page, rows.Count, pageRows), ct);
    }
    // ════════════════════════════════════════════
    //  Formatting helpers
    // ════════════════════════════════════════════

    private string BuildUpcomingHtml(Language lang, IEnumerable<UpcomingRow> items)
    {
        var sb = new StringBuilder();
        sb.AppendLine(_i18n.GetText(lang, "upcoming_birthdays"));
        sb.AppendLine();

        foreach (var i in items)
        {
            var dayStr = $"{i.NextDate.Day:D2}.{i.NextDate.Month:D2}";
            var relation = string.IsNullOrWhiteSpace(i.Relation) ? "" : $" · {Formatting.Html(i.Relation)}";
            sb.AppendLine($"🎂 <b>{Formatting.Html(i.Name)}</b> — {dayStr}, " +
                          $"{i.Age} {YearWord(lang, i.Age)}{relation}");
        }

        return sb.ToString();
    }

    private record struct UpcomingRow(string Name, DateOnly BirthDate, LocalDate NextDate, int Age, string? Relation);
    private record struct BirthdayListRow(Birthday Birthday, LocalDate NextDate, int Age);

    private List<BirthdayListRow> BuildBirthdayRows(IEnumerable<Birthday> birthdays, LocalDate today)
    {
        return birthdays
            .Select(b =>
            {
                var (next, age) = DateHelpers.NextBirthday(today, b.Date);
                return new BirthdayListRow(b, next, age);
            })
            .OrderBy(x => x.NextDate)
            .ThenBy(x => x.Birthday.FullName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private InlineKeyboardMarkup BuildAllEntriesKeyboard(Language lang, int page, int total)
    {
        var rows = new List<InlineKeyboardButton[]>();
        AddPagingButtons(rows, lang, "list:all", page, total);
        rows.Add(new[]
        {
            InlineKeyboardButton.WithCallbackData(_i18n.GetText(lang, "delete_entry"), "delete:list:0")
        });
        rows.Add(new[]
        {
            InlineKeyboardButton.WithCallbackData(_i18n.GetText(lang, "back_to_menu"), "menu:home")
        });
        return new InlineKeyboardMarkup(rows);
    }

    private InlineKeyboardMarkup BuildDeleteListKeyboard(
        Language lang,
        int page,
        int total,
        IReadOnlyList<BirthdayListRow> pageRows)
    {
        var rows = new List<InlineKeyboardButton[]>();
        for (var i = 0; i < pageRows.Count; i += 5)
        {
            rows.Add(pageRows
                .Skip(i)
                .Take(5)
                .Select((row, index) =>
                    InlineKeyboardButton.WithCallbackData(
                        (page * PageSize + i + index + 1).ToString(CultureInfo.InvariantCulture),
                        $"delete:pick:{row.Birthday.Id}:{page}"))
                .ToArray());
        }

        AddPagingButtons(rows, lang, "delete:list", page, total);
        rows.Add(new[]
        {
            InlineKeyboardButton.WithCallbackData(_i18n.GetText(lang, "all_records_button"), $"list:all:{page}")
        });
        rows.Add(new[]
        {
            InlineKeyboardButton.WithCallbackData(_i18n.GetText(lang, "back_to_menu"), "menu:home")
        });
        return new InlineKeyboardMarkup(rows);
    }

    private InlineKeyboardMarkup BuildDeleteConfirmKeyboard(Language lang, ObjectId id, int page)
    {
        return new InlineKeyboardMarkup(new[]
        {
            new[]
            {
                InlineKeyboardButton.WithCallbackData(_i18n.GetText(lang, "delete_confirm_button"), $"delete:confirm:{id}:{page}")
            },
            new[]
            {
                InlineKeyboardButton.WithCallbackData(_i18n.GetText(lang, "delete_cancel_button"), $"delete:cancel:{page}")
            }
        });
    }

    private InlineKeyboardMarkup BuildMonthNavigator(Language lang, int year, int month)
    {
        var prevMonth = month == 1 ? 12 : month - 1;
        var prevYear = month == 1 ? year - 1 : year;
        var nextMonth = month == 12 ? 1 : month + 1;
        var nextYear = month == 12 ? year + 1 : year;

        return new InlineKeyboardMarkup(new[]
        {
            new[]
            {
                InlineKeyboardButton.WithCallbackData(
                    $"◀️ {_i18n.GetText(lang, $"month_name_{prevMonth}")[..3]}",
                    $"list:month:{prevYear}-{prevMonth:D2}"),
                InlineKeyboardButton.WithCallbackData(
                    $"📅 {_i18n.GetText(lang, $"month_name_{month}")} {year}",
                    "cal:ignore"),
                InlineKeyboardButton.WithCallbackData(
                    $"{_i18n.GetText(lang, $"month_name_{nextMonth}")[..3]} ▶️",
                    $"list:month:{nextYear}-{nextMonth:D2}"),
            },
            new[]
            {
                InlineKeyboardButton.WithCallbackData(_i18n.GetText(lang, "all_records_button"), "list:all:0"),
                InlineKeyboardButton.WithCallbackData(_i18n.GetText(lang, "back_to_menu"), "menu:home"),
            }
        });
    }

    private void AddPagingButtons(List<InlineKeyboardButton[]> rows, Language lang, string callbackPrefix, int page, int total)
    {
        var lastPage = Math.Max(0, (total - 1) / PageSize);
        if (lastPage <= 0)
        {
            return;
        }

        var buttons = new List<InlineKeyboardButton>();
        if (page > 0)
        {
            buttons.Add(InlineKeyboardButton.WithCallbackData(_i18n.GetText(lang, "prev_page"), $"{callbackPrefix}:{page - 1}"));
        }

        buttons.Add(InlineKeyboardButton.WithCallbackData($"{page + 1}/{lastPage + 1}", "cal:ignore"));

        if (page < lastPage)
        {
            buttons.Add(InlineKeyboardButton.WithCallbackData(_i18n.GetText(lang, "next_page"), $"{callbackPrefix}:{page + 1}"));
        }

        rows.Add(buttons.ToArray());
    }

    private static int ClampPage(int page, int total)
    {
        if (total <= 0)
        {
            return 0;
        }

        var lastPage = (total - 1) / PageSize;
        return Math.Clamp(page, 0, lastPage);
    }

    private string YearWord(Language lang, int age)
    {
        return lang switch
        {
            Language.Ru => Formatting.PluralYears(age),
            Language.Pl => "lat",
            _ => age == 1 ? "year" : "years"
        };
    }

    // ════════════════════════════════════════════
    //  Free-text intent routing
    // ════════════════════════════════════════════

    private async Task<bool> TryHandleIntentAsync(
        BirthdayBot.Domain.Entities.User user,
        Telegram.Bot.Types.User tgUser,
        long chatId,
        string text,
        CancellationToken ct)
    {
        var parseSw = Stopwatch.StartNew();
        var intent = await _intentRouter.ParseAsync(user, text, ct);
        parseSw.Stop();

        var isFallback = intent.Intent is UserIntentType.None;
        await _aiEvents.CreateAsync(new AiEvent
        {
            UserId = user.Id,
            TelegramUserId = user.TelegramUserId,
            EventType = "intent",
            InputText = text,
            ParsedIntent = intent.Intent.ToString(),
            Confidence = intent.Confidence,
            IsFallback = isFallback,
            FallbackReason = isFallback ? "no_match" : null,
            PromptVersion = _promptProfiles.IntentPromptVersion,
            ModelSource = "local-intent-router",
            LatencyMs = parseSw.Elapsed.TotalMilliseconds
        }, ct);

        switch (intent.Intent)
        {
            case UserIntentType.None:
                return false;

            case UserIntentType.OpenHelp:
                await _bot.SendTextMessageAsync(chatId, _i18n.GetText(user.Lang, "help"),
                    parseMode: ParseMode.Html,
                    replyMarkup: Keyboards.BackToMenuKb(user.Lang),
                    cancellationToken: ct);
                return true;

            case UserIntentType.OpenSettings:
                await _bot.SendTextMessageAsync(chatId, BuildSettingsMessage(user),
                    parseMode: ParseMode.Html,
                    replyMarkup: Keyboards.SettingsKb(user.Lang, user),
                    cancellationToken: ct);
                return true;

            case UserIntentType.OpenAddBirthday:
                await _wizard.TryHandleAsync(new Update
                {
                    Message = new Message
                    {
                        Chat = new Chat { Id = chatId },
                        From = tgUser,
                        Text = "/add_birthday"
                    }
                }, ct);
                return true;

            case UserIntentType.OpenList:
                await SendCurrentMonthView(user, chatId, ct);
                return true;

            case UserIntentType.RemoveByName when !string.IsNullOrWhiteSpace(intent.EntityName):
                var encodedName = WebUtility.UrlEncode(intent.EntityName);
                await _bot.SendTextMessageAsync(
                    chatId,
                    string.Format(_i18n.GetText(user.Lang, "delete_confirm_by_name"), Formatting.Html(intent.EntityName)),
                    parseMode: ParseMode.Html,
                    replyMarkup: new InlineKeyboardMarkup(new[]
                    {
                        new[]
                        {
                            InlineKeyboardButton.WithCallbackData(
                                _i18n.GetText(user.Lang, "delete_confirm_button"),
                                $"delete:name:confirm:{encodedName}")
                        },
                        new[]
                        {
                            InlineKeyboardButton.WithCallbackData(
                                _i18n.GetText(user.Lang, "delete_cancel_button"),
                                "delete:name:cancel")
                        }
                    }),
                    cancellationToken: ct);
                return true;

            case UserIntentType.UpdateSettings when intent.Settings is not null:
                await ApplySettingsUpdateAsync(user, chatId, intent.Settings, ct);
                return true;

            default:
                _metrics.TrackIntentFallback("unknown_intent");
                return false;
        }
    }

    private async Task ApplySettingsUpdateAsync(
        BirthdayBot.Domain.Entities.User user,
        long chatId,
        SettingsUpdate update,
        CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(update.TimeHHmm))
        {
            user.NotifyAtLocalTime = update.TimeHHmm;
        }

        if (update.Lang.HasValue)
        {
            user.Lang = update.Lang.Value;
        }

        if (update.Tone.HasValue)
        {
            user.Tone = update.Tone.Value;
        }

        if (update.AutoGenerate.HasValue)
        {
            user.AutoGenerateGreetings = update.AutoGenerate.Value;
        }

        if (!string.IsNullOrWhiteSpace(update.Timezone) && _tzdb.Ids.Contains(update.Timezone))
        {
            user.Timezone = update.Timezone;
        }

        await _users.UpdateAsync(user, ct);
        await _bot.SendTextMessageAsync(chatId, _i18n.GetText(user.Lang, "saved"),
            replyMarkup: Keyboards.BackToMenuKb(user.Lang), cancellationToken: ct);
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
            Lang = Language.En,
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

    private async Task SendOrEditAsync(
        long chatId,
        int? messageId,
        string text,
        ParseMode? parseMode,
        IReplyMarkup? replyMarkup,
        CancellationToken ct)
    {
        if (messageId.HasValue && replyMarkup is InlineKeyboardMarkup inlineMarkup)
        {
            await SafeEditMessageAsync(chatId, messageId.Value, text, parseMode, inlineMarkup, ct);
            return;
        }

        await _bot.SendTextMessageAsync(chatId,
            text,
            parseMode: parseMode,
            replyMarkup: replyMarkup,
            cancellationToken: ct);
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

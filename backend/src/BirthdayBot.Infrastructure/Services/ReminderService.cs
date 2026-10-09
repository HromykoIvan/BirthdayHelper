using BirthdayBot.Application.Interfaces;
using BirthdayBot.Application.UI;
using BirthdayBot.Domain.Entities;
using BirthdayBot.Domain.Enums;
using NodaTime;
using System.Diagnostics;
using Telegram.Bot;
using Telegram.Bot.Types.ReplyMarkups;
using Microsoft.Extensions.Logging;

namespace BirthdayBot.Infrastructure.Services;

public sealed class ReminderService : IReminderService
{
    private readonly ILogger<ReminderService> _logger;
    private readonly IUserRepository _users;
    private readonly IBirthdayRepository _birthdays;
    private readonly IDeliveryLogRepository _logs;
    private readonly IGreetingGenerator _greetings;
    private readonly ILocalizationService _i18n;
    private readonly ITelegramBotClient _bot;
    private readonly IAiGreetingEnhancer _enhancer;
    private readonly IAiEventRepository _aiEvents;
    private readonly IDateTimeZoneProvider _tzdb = DateTimeZoneProviders.Tzdb;

    public ReminderService(
        ILogger<ReminderService> logger,
        IUserRepository users,
        IBirthdayRepository birthdays,
        IDeliveryLogRepository logs,
        IGreetingGenerator greetings,
        IAiGreetingEnhancer enhancer,
        IAiEventRepository aiEvents,
        ILocalizationService i18n,
        ITelegramBotClient bot)
    {
        _logger = logger;
        _users = users;
        _birthdays = birthdays;
        _logs = logs;
        _greetings = greetings;
        _enhancer = enhancer;
        _aiEvents = aiEvents;
        _i18n = i18n;
        _bot = bot;
    }

    public async Task RunOnceAsync(CancellationToken ct)
    {
        var users = await _users.ListAllAsync(ct);

        foreach (var user in users)
        {
            try
            {
                if (!_tzdb.Ids.Contains(user.Timezone))
                {
                    continue;
                }

                var zone = _tzdb[user.Timezone];
                var nowZoned = SystemClock.Instance.GetCurrentInstant().InZone(zone);
                var todayLocal = nowZoned.Date;

                if (!BirthdayBot.Domain.Utils.DateHelpers.TryParseTimeHHmm(user.NotifyAtLocalTime, out var hh, out var mm))
                {
                    continue;
                }

                if (nowZoned.TimeOfDay.Hour != hh || nowZoned.TimeOfDay.Minute != mm)
                {
                    continue;
                }

                var list = await _birthdays.ListByUserAsync(user.Id, ct);
                foreach (var birthday in list)
                {
                    var (next, age) = BirthdayBot.Domain.Utils.DateHelpers.NextBirthdayOptionalAge(
                        todayLocal,
                        birthday.Date,
                        birthday.HasKnownBirthYear);
                    var isToday = next == todayLocal;
                    var isTomorrow = next == todayLocal.PlusDays(1);

                    if (!isToday && !isTomorrow)
                    {
                        continue;
                    }

                    var when = isToday
                        ? user.Lang == Language.Pl ? "DZIŚ" : user.Lang == Language.Ru ? "СЕГОДНЯ" : "TODAY"
                        : user.Lang == Language.Pl ? "JUTRO" : user.Lang == Language.Ru ? "ЗАВТРА" : "TOMORROW";

                    var ageSuffix = age.HasValue ? $" ({age.Value})" : "";
                    var message = $"{when}: {birthday.FullName} — {next:yyyy-MM-dd}{ageSuffix}";
                    InlineKeyboardMarkup? replyMarkup = null;

                    if (user.AutoGenerateGreetings)
                    {
                        var draft = _greetings.GeneratePersonalized(user, birthday, age);
                        var enhanceSw = Stopwatch.StartNew();
                        var enhanced = await _enhancer.EnhanceAsync(user, birthday, draft, age, ct);
                        enhanceSw.Stop();

                        message += $"\n\n{enhanced.Text}";

                        var aiEvent = await _aiEvents.CreateAsync(new AiEvent
                        {
                            UserId = user.Id,
                            TelegramUserId = user.TelegramUserId,
                            BirthdayId = birthday.Id,
                            EventType = "enhance_reminder",
                            InputText = draft,
                            OutputText = enhanced.Text,
                            OutputVariants = enhanced.Variants?.ToDictionary(x => x.Style, x => x.Text),
                            IsFallback = enhanced.IsFallback,
                            FallbackReason = enhanced.FallbackReason,
                            PromptVersion = enhanced.PromptVersion,
                            ModelSource = enhanced.ModelSource,
                            InputTokens = enhanced.InputTokens,
                            OutputTokens = enhanced.OutputTokens,
                            LatencyMs = enhanceSw.Elapsed.TotalMilliseconds
                        }, ct);

                        replyMarkup = Keyboards.ReminderGreetingActionsKb(
                            user.Lang,
                            aiEvent.Id.ToString(),
                            enhanced.Variants is { Count: > 0 });
                    }

                    var sent = await _bot.SendTextMessageAsync(
                        chatId: user.TelegramUserId,
                        text: message,
                        replyMarkup: replyMarkup,
                        cancellationToken: ct);

                    await _logs.CreateAsync(new DeliveryLog
                    {
                        UserId = user.Id,
                        BirthdayId = birthday.Id,
                        WhenUtc = DateTime.UtcNow,
                        MessageId = sent.MessageId.ToString(),
                        Status = "Sent"
                    }, ct);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Reminder error for user {User}", user.TelegramUserId);
            }
        }
    }
}
